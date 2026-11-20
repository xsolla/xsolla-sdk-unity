#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using JetBrains.Annotations;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
using Xsolla.SDK.Utils;
using Xsolla.SDK.Store;
using Xsolla.SDK.Common;

namespace Xsolla.SDK.UnityPurchasing
{
    /// <summary>
    /// XsollaPurchasingStore
    /// </summary>
    internal class XsollaPurchasingStore : AbstractStore, IXsollaPurchasingStoreConfiguration, IXsollaPurchasingStoreExtension
    {
        public const string Name = "XsollaStore";
        
        private const string Tag = "XsollaPurchasingStore";
        
        [CanBeNull] private IStoreCallback _storeCallback;
        private readonly IXsollaStoreClient _storeClient;

        private readonly ISimpleFuture<XsollaClientConfiguration, string> _settingsFuture;
        [CanBeNull] private ISimpleFuture<bool, string> _initializedFuture;
        [CanBeNull] private ISimpleFuture<bool, string> _productsFuture;
        
        private readonly Dictionary<string, XsollaStoreClientProduct> _productById = new Dictionary<string, XsollaStoreClientProduct>();

        /// <summary>
        /// The type of every SKU a <see cref="RetrieveProducts"/> call was given, as the first such call gave it, which
        /// every restore reads to put a non-consumable back under the id Unity IAP logged for it, the recorded purchase id
        /// or else the native id (see <see cref="WithNativeIdsOfNonConsumables"/>), and every purchase to tell whether its
        /// holding is to be recorded.
        /// <para/>
        /// Unity IAP 4.x keeps the product it registered first when a later fetch of additional products registers the
        /// SKU again under another type, so a later call's type would differ from the one Unity IAP grants and
        /// finishes the purchase as. Same main-thread-only invariant as <see cref="_quantityByPurchase"/>.
        /// </summary>
        private readonly Dictionary<string, ProductType> _productTypeBySku = new Dictionary<string, ProductType>();

        /// <summary>
        /// Purchased quantity per (transaction, sku), recorded when the purchase is reported and drained in
        /// <see cref="FinishTransaction"/>.
        /// <para/>
        /// Unity IAP only hands <see cref="FinishTransaction"/> a transaction id, but consuming a collapsed
        /// multi-unit restore needs the purchased quantity. Keyed by sku as well, not by transaction id alone:
        /// a transaction id is not guaranteed to own a single sku (bundle and virtual-currency content lines
        /// share one), so a transaction-only key lets the last line overwrite the rest.
        /// <para/>
        /// Not synchronized: every access runs on the Unity main thread, because the standalone client delivers
        /// all web-request callbacks via coroutine continuations. Same invariant as <c>_productById</c> above.
        /// </summary>
        private readonly Dictionary<(string transactionId, string sku), int> _quantityByPurchase =
            new Dictionary<(string transactionId, string sku), int>();

        /// <summary>
        /// Where each (transaction, sku) stands in the consume pipeline, so a repeated
        /// <see cref="FinishTransaction"/> for a purchase already being (or already) consumed is dropped.
        /// <para/>
        /// Unity IAP finishes the same purchase more than once: its <c>ProcessPurchaseOnStart</c> walks the whole
        /// product catalog on every <c>OnProductsRetrieved</c> and re-finishes every transaction already in its
        /// log, with no <c>ProcessPurchase</c> in between — so any later <c>FetchAdditionalProducts</c> re-finishes
        /// an earlier restored purchase. Unguarded, that second finish consumes again: a real second inventory
        /// consume on Standalone and iOS, and on Android a consume whose purchase token the receipt cache has
        /// already spent.
        /// <para/>
        /// A failed consume drops its key so a later finish can retry; <see cref="ConsumeState.Done"/> is never
        /// cleared, making the guard session-scoped by design — the server stays the source of truth across runs.
        /// Same main-thread-only invariant as <see cref="_quantityByPurchase"/>.
        /// </summary>
        private readonly Dictionary<(string transactionId, string sku), ConsumeState> _consumeStateByPurchase =
            new Dictionary<(string transactionId, string sku), ConsumeState>();

        /// <summary>
        /// Stage of the consume pipeline tracked in <see cref="_consumeStateByPurchase"/>.
        /// </summary>
        private enum ConsumeState
        {
            /// <summary>A consume has been dispatched to the store client and has not called back yet.</summary>
            InFlight,

            /// <summary>The consume completed successfully; further finishes for this purchase are no-ops.</summary>
            Done
        }

        /// <summary>
        /// Every (transaction, sku) already handed to Unity IAP this session, so a duplicate delivery is dropped.
        /// <para/>
        /// Catches a native re-delivery of an owned, unconsumed item and a restore racing a purchase. A genuine
        /// re-purchase gets a new transaction id, so it is not blocked. Caveat: this also drops a re-delivery that
        /// would have retried a first delivery whose validation or consume failed; that purchase stays pending and
        /// resurfaces next session.
        /// Marked wherever a purchase reaches Unity IAP: in <see cref="OnPurchaseSucceeded"/> and as a receipt on a
        /// product in the first <see cref="RetrieveProducts"/>.
        /// Same main-thread-only invariant as <see cref="_quantityByPurchase"/>.
        /// </summary>
        private readonly HashSet<(string transactionId, string sku)> _deliveredPurchases =
            new HashSet<(string transactionId, string sku)>();

        /// <summary>
        /// The transaction id each purchase of a non-consumable was granted under, by the id the Android SDK made up for
        /// its holding, which every restore reads to hand the holding over under the id Unity IAP logged at the purchase
        /// (see <see cref="WithNativeIdsOfNonConsumables"/>). Null where the store client makes up no ids.
        /// </summary>
        [CanBeNull] private readonly XsollaNonConsumableTransactionIdsAndroid _nonConsumableTransactionIds;

        /// <summary>
        /// Every (transaction, sku) of a purchase of a non-consumable handed to Unity IAP this session and not finished
        /// yet, whose holding <see cref="FinishTransaction"/> records in <see cref="_nonConsumableTransactionIds"/>, with
        /// <see cref="_accessTokenUpdates"/> as it stood at the delivery.
        /// Same main-thread-only invariant as <see cref="_quantityByPurchase"/>.
        /// </summary>
        private readonly Dictionary<(string transactionId, string sku), long> _nonConsumablePurchasesToRecord =
            new Dictionary<(string transactionId, string sku), long>();

        /// <summary>
        /// The number of <see cref="UpdateAccessToken"/> calls, each of which may switch the store to another user.
        /// Same main-thread-only invariant as <see cref="_quantityByPurchase"/>.
        /// </summary>
        private long _accessTokenUpdates;

        [CanBeNull] private Action<IStoreCallback, string, string, string> onPurchaseSucceeded;
        [CanBeNull] private Action<IStoreCallback, PurchaseFailureDescription> onPurchaseFailed;
        
        [CanBeNull] private XsollaPurchasingStoreValidator _validator;

        public XsollaPurchasingStore(XsollaClientConfiguration configuration)
            : this(configuration, XsollaStoreClientFactory.Create(),
#if UNITY_ANDROID && !UNITY_EDITOR
                XsollaNonConsumableTransactionIdsAndroid.InPlayerPrefs()
#else
                null
#endif
            ) { }

        /// <summary>
        /// Takes the store client and the record of non-consumable purchases explicitly so tests can drive the store
        /// against a stub instead of the platform client <see cref="XsollaStoreClientFactory"/> would pick.
        /// </summary>
        internal XsollaPurchasingStore(
            XsollaClientConfiguration configuration,
            IXsollaStoreClient storeClient,
            [CanBeNull] XsollaNonConsumableTransactionIdsAndroid nonConsumableTransactionIds = null
        ) {
            _storeClient = storeClient;
            _nonConsumableTransactionIds = nonConsumableTransactionIds;

            RunOnStartThread.Create();

            _settingsFuture = SimpleFuture.Create<XsollaClientConfiguration, string>(out var promise);
            XsollaLogger.Apply(configuration);
            if (configuration.delayedTask != null)
            {
                AwaitForConfiguration(configuration, promise);
            }
            else 
                promise.Complete(configuration);
        }
        
        private async void AwaitForConfiguration(XsollaClientConfiguration configuration, ISimplePromise<XsollaClientConfiguration, string> promise)
        {
            var mapper = await configuration.delayedTask;
            RunOnStartThread.Run(() => promise.Complete(mapper(configuration)));
        }
        
        public override void Initialize(IStoreCallback callback)
        {
            XsollaLogger.Debug(Tag, "Initialize");
            
            _storeCallback = callback;

            var client = _storeClient;
            
            _initializedFuture = SimpleFuture.Create<bool, string>(out var promise);
            _settingsFuture.OnComplete(
                onSuccess: configuration => {
                    XsollaLogger.Apply(configuration);
                    
                    client.Initialize(
                        configuration, 
                        onSuccess: () => XsollaLogger.Debug(Tag, "Initialize finished"), 
                        onError: error => XsollaLogger.Debug(Tag, $"Initialize failed: {error}"),
                        onSuccessPurchaseProduct: OnPurchaseSucceeded, 
                        onErrorPurchase: error => OnPurchaseFailed(error, null)
                    );
                    promise.Complete(true);
                },
                onError: error => {
                    XsollaLogger.Error(Tag, $"Initialize failed: {error}");
                    promise.CompleteWithError(error);
                }
            );
        }

        public override void RetrieveProducts(ReadOnlyCollection<ProductDefinition> products)
        {
            XsollaLogger.Debug(Tag, "RetrieveProducts");

            // Recorded at the call rather than with the catalog, which a restore may answer before.
            foreach (var product in products)
            {
                if (!_productTypeBySku.ContainsKey(product.storeSpecificId))
                    _productTypeBySku.Add(product.storeSpecificId, product.type);
            }

            if (_initializedFuture == null) {
                XsollaLogger.Error(Tag, "RetrieveProducts: not initialized");
                _storeCallback?.OnProductsRetrieved(new List<ProductDescription>());
                return;
            }
            
            ISimplePromise<bool, string> promise = null;
            if (_productsFuture == null)
                _productsFuture = SimpleFuture.Create<bool, string>(out promise);

            _initializedFuture.OnComplete(
                onSuccess: _ => RetrieveProducts_(),
                onError: error => {
                    XsollaLogger.Error(Tag, $"RetrieveProducts failed: {error}");
                    _storeCallback?.OnProductsRetrieved(new List<ProductDescription>());
                }
            );

            void RetrieveProducts_() {
                var productsRestoreFuture =
                    SimpleFuture.Create<XsollaStoreClientPurchasedProduct[], string>(out var productsRestorePromise);
                var productsRequestFuture =
                    SimpleFuture.Create<XsollaStoreClientProduct[], string>(out var productsRequestPromise);
                var productsFuture = productsRestoreFuture.Zip(
                    productsRequestFuture, (inventory, store) => (inventory, store)
                );

#if UNITY_IOS
                // iOS restores automatically on start via the native SDK observer
                productsRestorePromise.Complete(new XsollaStoreClientPurchasedProduct[0]);
#else
                if (promise != null)
                {
                    _storeClient.RestorePurchases(
                        onSuccess: items => productsRestorePromise.Complete(items),
                        onError: error => productsRestorePromise.CompleteWithError(error)
                    );
                }
                else
                {
                    productsRestorePromise.Complete(new XsollaStoreClientPurchasedProduct[0]);
                }
#endif

                _storeClient.FetchProducts(products.MapToArray(product => product.storeSpecificId),
                    onSuccess: items => productsRequestPromise.Complete(items),
                    onError: error => productsRequestPromise.CompleteWithError(error)
                );

                productsFuture.OnComplete(
                    onSuccess: items => {
                        var inventory = WithNativeIdsOfNonConsumables(items.inventory);

                        XsollaLogger.Debug(Tag, $"RetrieveProducts finished successfully. " +
                                        $"Restored: {inventory.Length}, " +
                                        $"Retrieved: {items.store.Length} ( {XsollaClientHelpers.ToJson(items.store)} )");

                        //Store url for future use
                        foreach (var item in items.store)
                            _productById[item.sku] = item;
                        
                        _storeCallback?.OnProductsRetrieved(items.store.Map(item => {
                            var localizedPrice = (decimal) item.localizedPrice / 1_000_000;

                            if (inventory.FindFirst(it => it.sku == item.sku, out var purchasedItem)) {
                                // This restore path reaches Unity IAP through OnProductsRetrieved (one
                                // ProductDescription per SKU), not OnPurchaseSucceeded, so record the
                                // quantity here too. Without this, FinishTransaction misses the map and
                                // consumes 1. With collapse on, purchasedItem.quantity is the full count
                                // and the SKU drains in a single restore; in split mode it is always 1
                                // (only the first inventory row per SKU is surfaced here).
                                if (!string.IsNullOrEmpty(purchasedItem.transactionId))
                                {
                                    var trackedQuantity = purchasedItem.quantity > 0 ? purchasedItem.quantity : 1;
                                    _quantityByPurchase[(purchasedItem.transactionId, item.sku)] = trackedQuantity;
                                    // The receipt below is a delivery too: without the mark, a later RestoreTransactions or
                                    // native re-delivery, or a purchase callback queued behind this fetch, would hand it to
                                    // Unity IAP again.
                                    _deliveredPurchases.Add((purchasedItem.transactionId, item.sku));
                                    var finishOutcome = _productTypeBySku.TryGetValue(item.sku, out var productType) && productType != ProductType.Consumable
                                        ? $"{productType}, never consumed"
                                        : $"will consume {trackedQuantity} unit(s) on FinishTransaction";
                                    XsollaLogger.Debug(Tag, $"RetrieveProducts: tracking restored sku={item.sku} quantity={trackedQuantity} transactionId={purchasedItem.transactionId} ({finishOutcome})");
                                }

                                var receiptData = purchasedItem.ToReceipt().ToJson();

                                return new ProductDescription(
                                    id: item.productId,
                                    metadata: new ProductMetadata(
                                        priceString: item.localizedPriceString,
                                        title: item.localizedTitle,
                                        description: item.localizedDescription,
                                        currencyCode: item.currencyCode,
                                        localizedPrice
                                    ),
                                    receipt: receiptData,
                                    transactionId: purchasedItem.transactionId
                                );
                            }

                            return new ProductDescription(
                                id: item.sku,
                                metadata: new ProductMetadata(
                                    priceString: item.localizedPriceString,
                                    title: item.localizedTitle,
                                    description: item.localizedDescription,
                                    currencyCode: item.currencyCode,
                                    localizedPrice
                                )
                            );
                        }).ToList());
                        
                        promise?.Complete(true);
                    },
                    onError: error => {
                        XsollaLogger.Error(Tag, $"RetrieveProducts failed: {error}");
                        _storeCallback?.OnProductsRetrieved(new List<ProductDescription>());
                        
                        promise?.Complete(false);
                    }
                );
            }
        }

        public override void Purchase(ProductDefinition product, string developerPayload) =>
            Purchase(product, developerPayload, XsollaStoreClientPurchaseArgs.Empty);
        
        private void Purchase(ProductDefinition product, string developerPayload, XsollaStoreClientPurchaseArgs args) =>
            Purchase(product.storeSpecificId, developerPayload, args);
        
        private void Purchase(string sku, string developerPayload, XsollaStoreClientPurchaseArgs args)
        {
            XsollaLogger.Debug(Tag, "Purchase");

            _storeClient.PurchaseProduct(sku, developerPayload, args,
                onSuccess: OnPurchaseSucceeded,
                onError: error => OnPurchaseFailed(error, sku)
            );
        }
        
        private void OnPurchaseSucceeded(XsollaStoreClientPurchasedProduct product)
        {
            XsollaLogger.Debug(Tag, $"Purchase finished: {product}");

            // A duplicate delivery must neither overwrite the tracked quantity nor re-add one a consume already drained.
            if (!string.IsNullOrEmpty(product.transactionId) && !_deliveredPurchases.Contains((product.transactionId, product.sku)))
                _quantityByPurchase[(product.transactionId, product.sku)] = product.quantity > 0 ? product.quantity : 1;

            _productsFuture?.OnComplete(
                onSuccess: loaded =>
                {
                    // Marked here rather than on entry: a purchase reported before the first RetrieveProducts never
                    // reaches Unity IAP, and marking it would make a later RestoreTransactions drop it as well.
                    if (!string.IsNullOrEmpty(product.transactionId) &&
                        !_deliveredPurchases.Add((product.transactionId, product.sku)))
                    {
                        XsollaLogger.Warning(Tag, $"Duplicate delivery ignored: transactionId={product.transactionId} sku={product.sku}");
                        return;
                    }

                    if (_nonConsumableTransactionIds != null
                        && product.status != XsollaStoreClientPurchasedProduct.Status.Restored
                        && !string.IsNullOrEmpty(product.transactionId)
                        && product.sku != null
                        && _productTypeBySku.TryGetValue(product.sku, out var productType)
                        && productType != ProductType.Consumable)
                    {
                        _nonConsumablePurchasesToRecord[(product.transactionId, product.sku)] = _accessTokenUpdates;
                    }

                    var receiptData = product.ToReceipt().ToJson();

                    if (onPurchaseSucceeded != null)
                        onPurchaseSucceeded(_storeCallback, product.sku, receiptData, product.transactionId);
                    else
                        _storeCallback?.OnPurchaseSucceeded(
                            storeSpecificId: product.sku,
                            receipt: receiptData,
                            transactionIdentifier: product.transactionId
                        );
                },
                onError: error =>
                {
                    XsollaLogger.Error(Tag, $"OnPurchaseSucceeded failed: {error}");
                }
            );
        }
        
        private void OnPurchaseFailed(string error, [CanBeNull] string sku)
        {
            var parsed = XsollaStoreClientHelpers.ParsePurchaseError(error);
            var reason = MapToIapReason(parsed.code);

            if (reason == PurchaseFailureReason.UserCancelled) {
                XsollaLogger.Warning(Tag, $"Purchase failed: {parsed.message}, reason: {reason}, sku: {sku}");
            } else {
                XsollaLogger.Error(Tag, $"Purchase failed: {parsed.message}, reason: {reason}, sku: {sku}");
            }

            if (sku == null) return;

            var desc = new PurchaseFailureDescription(
                productId: sku,
                reason: reason,
                message: parsed.message
            );
                    
            if (onPurchaseFailed != null)
                onPurchaseFailed(_storeCallback, desc);
            else 
                _storeCallback?.OnPurchaseFailed(desc);
        }

        private PurchaseFailureReason MapToIapReason(XsollaStoreClientPurchaseErrorCode code)
        {
            switch (code)
            {
                case XsollaStoreClientPurchaseErrorCode.Cancelled:
                    return PurchaseFailureReason.UserCancelled;
                default:
                    return PurchaseFailureReason.PaymentDeclined;
            }
        }

        public override void FinishTransaction(ProductDefinition product, string transactionId)
        {
            var sku = product.storeSpecificId;

            var purchase = (transactionId, sku);

            // An empty transaction id cannot be tracked in either dictionary, so such a finish is neither
            // guarded nor remembered — it consumes one unit, which is what it did before the guard existed.
            var isTracked = !string.IsNullOrEmpty(transactionId);

            if (product.type != ProductType.Consumable)
            {
                XsollaLogger.Debug(Tag, $"FinishTransaction: sku={sku} transactionId={transactionId} type={product.type} — not consumable, nothing to consume.");

                // Non-consumables are never consumed, so drop any tracked quantity here to avoid leaking the entry.
                if (isTracked)
                    _quantityByPurchase.Remove(purchase);

                if (_nonConsumableTransactionIds != null
                    && _nonConsumablePurchasesToRecord.TryGetValue(purchase, out var accessTokenUpdatesAtDelivery))
                {
                    _nonConsumablePurchasesToRecord.Remove(purchase);
                    RecordHoldingOfNonConsumablePurchase(_nonConsumableTransactionIds, sku, transactionId, accessTokenUpdatesAtDelivery);
                }

                return;
            }

            if (isTracked && _consumeStateByPurchase.TryGetValue(purchase, out var consumeState))
            {
                XsollaLogger.Debug(Tag, $"FinishTransaction: ignoring a duplicate finish (state={consumeState}) sku={sku} transactionId={transactionId}");
                return;
            }

            int quantity;
            string quantitySource;
            if (isTracked && _quantityByPurchase.TryGetValue(purchase, out var tracked))
            {
                quantity = tracked;
                quantitySource = quantity > 1 ? "collapsed multi-unit" : "tracked single unit";
            }
            else
            {
                quantity = 1;
                quantitySource = "untracked, defaulting to 1";
            }

            XsollaLogger.Debug(Tag, $"FinishTransaction: consuming sku={sku} quantity={quantity} ({quantitySource}) transactionId={transactionId}");

            if (isTracked)
                _consumeStateByPurchase[purchase] = ConsumeState.InFlight;

            _storeClient.ConsumeProduct(sku, quantity, transactionId,
                onSuccess: () =>
                {
                    if (isTracked)
                    {
                        _consumeStateByPurchase[purchase] = ConsumeState.Done;

                        // Drop the quantity only after a confirmed consume so a retried finish still drains the
                        // full multi-unit quantity instead of defaulting to 1.
                        _quantityByPurchase.Remove(purchase);
                    }

                    XsollaLogger.Debug(Tag, $"FinishTransaction finished: sku={sku} consumed {quantity} unit(s) in a single consume.");
                },
                onError: error =>
                {
                    // Release the in-flight mark so a later finish can retry this purchase, and keep its tracked
                    // quantity for that retry.
                    if (isTracked)
                        _consumeStateByPurchase.Remove(purchase);

                    XsollaLogger.Error(Tag, $"FinishTransaction failed: sku={sku} quantity={quantity} transactionId={transactionId}: {error}");
                }
            );
        }

        public void RestoreTransactions(Action<bool> onSuccess, Action<string> onError)
        {
            XsollaLogger.Debug(Tag, "RestoreTransactions");
            
            _storeClient.RestorePurchases(
                onSuccess: items =>
                {
                    foreach (var item in WithNativeIdsOfNonConsumables(items))
                    {
                        //if (item.VirtualItemType != VirtualItemType.Consumable)
                        //    continue;
                        
                        OnPurchaseSucceeded(item);
                    }
                    
                    onSuccess?.Invoke(items.Length > 0);
                },
                onError: error => onError?.Invoke(error.ToString())
            );
        }

        /// <summary>
        /// The restored <paramref name="items"/>, with each identity-less one of a SKU typed
        /// <see cref="ProductType.NonConsumable"/> or <see cref="ProductType.Subscription"/> put back under the id Unity
        /// IAP's transaction log holds for it: the transaction id of the purchase this device granted it as, recorded in
        /// <see cref="_nonConsumableTransactionIds"/>, or else the id the Android SDK made up for it, which earlier
        /// launches restored it under. The others as they are.
        /// <para/>
        /// <see cref="XsollaRestoredTransactionIdsAndroid"/> keeps the made-up id as the invoice id, and the Android SDK
        /// reported it as order and transaction id alike, so the purchase reads as the Android SDK delivered it. The
        /// store client's receipt cache keeps its line under the id it gave, which only a consume reads, and a
        /// non-consumable is never consumed.
        /// </summary>
        private XsollaStoreClientPurchasedProduct[] WithNativeIdsOfNonConsumables(XsollaStoreClientPurchasedProduct[] items) =>
            items.Map(item =>
            {
                if (item.sku != null
                    && _productTypeBySku.TryGetValue(item.sku, out var productType)
                    && productType != ProductType.Consumable
                    && HasMadeUpId(item, out var nativeId))
                {
                    var transactionId = _nonConsumableTransactionIds?.Find(item.sku, item.invoiceId) ?? item.invoiceId;

                    XsollaLogger.Debug(Tag, $"Restored {productType} under the id Unity IAP logged: sku={item.sku} transactionId={item.transactionId} nativeTransactionId={item.invoiceId} -> transactionId={transactionId}");

                    return XsollaStoreClientPurchasedProduct.Builder.Create()
                        .SetSku(item.sku)
                        .SetOrderId(nativeId)
                        .SetInvoiceId(item.invoiceId)
                        .SetTransactionId(transactionId)
                        .SetReceipt(item.receipt)
                        .SetQuantity(item.quantity)
                        .SetPurchaseTimeMillis(item.purchaseTimeMillis)
                        .SetStatus(item.status)
                        .SetOrigin(item.origin)
                        .SetDeveloperPayload(item.developerPayload)
                        .Build();
                }
                else
                {
                    return item;
                }
            });

        /// <summary>
        /// Reads the inventory and records <paramref name="transactionId"/>, a purchase of non-consumable
        /// <paramref name="sku"/> Unity IAP has just logged, for every unit of <paramref name="sku"/> the Android SDK
        /// made up an id for, so the next launches restore the holding under the id Unity IAP logged and do not grant it
        /// again.
        /// <para/>
        /// Read now rather than at the next restore: the made-up id names the user who holds the item now, and a later
        /// restore may read another user's holding. Every unit of the SKU is recorded, as a non-consumable is either
        /// owned or not: a unit held before this purchase reached Unity IAP through an earlier restore already.
        /// <para/>
        /// Nothing is recorded under an access token that names no user, whose made-up ids every user shares, nor once
        /// <see cref="UpdateAccessToken"/> was called after the delivery, which <paramref name="accessTokenUpdatesAtDelivery"/>
        /// tells, as the read may then answer with another user's holding: either record would hand that user's restore to
        /// Unity IAP under an id it already logged, which it never grants. A read that is skipped, fails or lands before
        /// the inventory shows the purchase records nothing, and the next launch grants the item once more, as before.
        /// </summary>
        private void RecordHoldingOfNonConsumablePurchase(
            [NotNull] XsollaNonConsumableTransactionIdsAndroid nonConsumableTransactionIds,
            string sku,
            string transactionId,
            long accessTokenUpdatesAtDelivery
        ) {
            _storeClient.GetAccessToken(
                onSuccess: token =>
                {
                    if (XsollaNonConsumableTransactionIdsAndroid.NamesUser(token))
                    {
                        _storeClient.RestorePurchases(
                            onSuccess: items =>
                            {
                                if (_accessTokenUpdates == accessTokenUpdatesAtDelivery)
                                    RecordHolding(nonConsumableTransactionIds, sku, transactionId, items);
                                else
                                    XsollaLogger.Warning(Tag, $"The access token was updated since the purchase, so its holding is not recorded and its next restore grants it again: sku={sku} transactionId={transactionId}");
                            },
                            onError: error => XsollaLogger.Warning(Tag, $"Could not read the holding of a purchased non-consumable, its next restore grants it again: sku={sku} transactionId={transactionId} error={error}")
                        );
                    }
                    else
                    {
                        XsollaLogger.Warning(Tag, $"The access token names no user, so the holding of a purchased non-consumable is not recorded and its next restore grants it again: sku={sku} transactionId={transactionId}");
                    }
                },
                onError: error => XsollaLogger.Warning(Tag, $"Could not read the access token, so the holding of a purchased non-consumable is not recorded and its next restore grants it again: sku={sku} transactionId={transactionId} error={error}")
            );
        }

        /// <summary>
        /// Records <paramref name="transactionId"/> for every unit of <paramref name="sku"/> in the inventory
        /// <paramref name="items"/> the Android SDK made up an id for.
        /// </summary>
        private static void RecordHolding(
            [NotNull] XsollaNonConsumableTransactionIdsAndroid nonConsumableTransactionIds,
            string sku,
            string transactionId,
            XsollaStoreClientPurchasedProduct[] items
        ) {
            var recorded = 0;
            foreach (var item in items)
            {
                if (item.sku == sku && HasMadeUpId(item, out _))
                {
                    nonConsumableTransactionIds.Record(sku, item.invoiceId, transactionId);
                    recorded++;
                }
            }

            XsollaLogger.Debug(Tag, $"Recorded the holding of a purchased non-consumable: sku={sku} transactionId={transactionId} units={recorded}");
        }

        /// <summary>
        /// Whether <paramref name="item"/> is a restored unit whose ids the Android SDK made up, kept by
        /// <see cref="XsollaRestoredTransactionIdsAndroid"/> as the invoice id, which is given as
        /// <paramref name="nativeId"/>.
        /// </summary>
        private static bool HasMadeUpId(XsollaStoreClientPurchasedProduct item, out long nativeId) =>
            long.TryParse(item.invoiceId, NumberStyles.None, CultureInfo.InvariantCulture, out nativeId)
            && nativeId > 0
            && item.identityless;

        public bool TryGetProductIconUrl(Product product, out string url)
        {
            var res = _productById.TryGetValue(product.definition.storeSpecificId, out var productData);
            if (res)
            {
                url = productData.iconUrl;
                return true;
            }
            
            url = null;
            return false;
        }

        public bool TryGetProduct(Product product, out XsollaStoreClientProduct productData)
        {
            return _productById.TryGetValue(product.definition.storeSpecificId, out productData);
        }

        public void SetCustomPurchaseFlowCallbacks(
            [CanBeNull] Action<IStoreCallback, string, string, string> onPurchaseSucceeded,
            [CanBeNull] Action<IStoreCallback, PurchaseFailureDescription> onPurchaseFailed
        ) {
            this.onPurchaseSucceeded = onPurchaseSucceeded;
            this.onPurchaseFailed = onPurchaseFailed;
        }

        public XsollaPurchasingStoreValidator GetValidator()
        {
            if (_validator == null)
                _validator = new XsollaPurchasingStoreValidator(_storeClient);
            return _validator;
        }

        public void GetAccessToken(Action<string> onSuccess, Action<string> onError)
        {
            XsollaLogger.Debug(Tag, "GetAccessToken");
            
            _storeClient.GetAccessToken(
                onSuccess: token => onSuccess?.Invoke(token),
                onError: error => onError?.Invoke(error)
            );
        }

        public void UpdateAccessToken(string token, Action onSuccess, Action<string> onError)
        {
            XsollaLogger.Debug(Tag, "UpdateAccessToken");

            _accessTokenUpdates++;
            
            _storeClient.UpdateAccessToken(
                token,
                onSuccess: () => onSuccess?.Invoke(),
                onError: error => onError?.Invoke(error)
            );
        }

        public void GetAppleStorefront(Action<string> onSuccess, Action<string> onError)
        {
            XsollaLogger.Debug(Tag, "GetAppleStorefront");
            
            _storeClient.GetAppleStorefront(
                onSuccess: storefront => onSuccess?.Invoke(storefront),
                onError: error => onError?.Invoke(error)
            );
        }

        public void InitiatePurchase(Product product, XsollaStoreClientPurchaseArgs args)
        {
            Purchase(product.definition.storeSpecificId, null, args);
        }

        public void InitiatePurchase(string productId, XsollaStoreClientPurchaseArgs args)
        {
            Purchase(productId, null, args);
        }

    }
}
#endif