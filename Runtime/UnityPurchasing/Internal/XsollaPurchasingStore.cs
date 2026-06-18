#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private readonly IXsollaStoreClient _storeClient = XsollaStoreClientFactory.Create();

        private readonly ISimpleFuture<XsollaClientConfiguration, string> _settingsFuture;
        [CanBeNull] private ISimpleFuture<bool, string> _initializedFuture;
        [CanBeNull] private ISimpleFuture<bool, string> _productsFuture;
        
        private readonly Dictionary<string, XsollaStoreClientProduct> _productById = new Dictionary<string, XsollaStoreClientProduct>();

        // Unity IAP only hands FinishTransaction a transaction ID, but consuming a collapsed
        // multi-unit restore needs the purchased quantity. Remember it per transaction here when the
        // purchase is reported, then drain that many units in FinishTransaction.
        // Not synchronized: every access (OnPurchaseSucceeded, FinishTransaction) runs on the Unity
        // main thread, because the standalone client delivers all web-request callbacks via coroutine
        // continuations. Same invariant as _productById above.
        private readonly Dictionary<string, int> _quantityByTransactionId = new Dictionary<string, int>();

        [CanBeNull] private Action<IStoreCallback, string, string, string> onPurchaseSucceeded;
        [CanBeNull] private Action<IStoreCallback, PurchaseFailureDescription> onPurchaseFailed;
        
        [CanBeNull] private XsollaPurchasingStoreValidator _validator;

        public XsollaPurchasingStore(XsollaClientConfiguration configuration) {
            RunOnStartThread.Create();
            
            _settingsFuture = SimpleFuture.Create<XsollaClientConfiguration, string>(out var promise);
            XsollaLogger.SetLogLevel(configuration.logLevel);
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
                    XsollaLogger.SetLogLevel(configuration.logLevel);
                    
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
                        XsollaLogger.Debug(Tag, $"RetrieveProducts finished successfully. " +
                                        $"Restored: {items.inventory.Length}, " +
                                        $"Retrieved: {items.store.Length} ( {XsollaClientHelpers.ToJson(items.store)} )");

                        //Store url for future use
                        foreach (var item in items.store)
                            _productById[item.sku] = item;
                        
                        _storeCallback?.OnProductsRetrieved(items.store.Map(item => {
                            var localizedPrice = (decimal) item.localizedPrice / 1_000_000;

                            if (items.inventory.FindFirst(it => it.sku == item.sku, out var purchasedItem)) {
                                // This restore path reaches Unity IAP through OnProductsRetrieved (one
                                // ProductDescription per SKU), not OnPurchaseSucceeded, so record the
                                // quantity here too. Without this, FinishTransaction misses the map and
                                // consumes 1. With collapse on, purchasedItem.quantity is the full count
                                // and the SKU drains in a single restore; in split mode it is always 1
                                // (only the first inventory row per SKU is surfaced here).
                                if (!string.IsNullOrEmpty(purchasedItem.transactionId))
                                {
                                    var trackedQuantity = purchasedItem.quantity > 0 ? purchasedItem.quantity : 1;
                                    _quantityByTransactionId[purchasedItem.transactionId] = trackedQuantity;
                                    XsollaLogger.Debug(Tag, $"RetrieveProducts: tracking restored sku={item.sku} quantity={trackedQuantity} transactionId={purchasedItem.transactionId} (will consume {trackedQuantity} unit(s) on FinishTransaction)");
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

            if (!string.IsNullOrEmpty(product.transactionId))
                _quantityByTransactionId[product.transactionId] = product.quantity > 0 ? product.quantity : 1;

            _productsFuture?.OnComplete(
                onSuccess: loaded =>
                {
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

            if (product.type != ProductType.Consumable)
            {
                XsollaLogger.Debug(Tag, $"FinishTransaction: sku={sku} transactionId={transactionId} type={product.type} — not consumable, nothing to consume.");

                // Non-consumables are never consumed, so drop any tracked quantity here to avoid leaking the entry.
                if (!string.IsNullOrEmpty(transactionId))
                    _quantityByTransactionId.Remove(transactionId);

                return;
            }

            int quantity;
            string quantitySource;
            if (!string.IsNullOrEmpty(transactionId) && _quantityByTransactionId.TryGetValue(transactionId, out var tracked))
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

            _storeClient.ConsumeProduct(sku, quantity, transactionId,
                onSuccess: () =>
                {
                    // Drop the entry only after a confirmed consume so a failed or repeated
                    // FinishTransaction still drains the full multi-unit quantity instead of defaulting to 1.
                    if (!string.IsNullOrEmpty(transactionId))
                        _quantityByTransactionId.Remove(transactionId);

                    XsollaLogger.Debug(Tag, $"FinishTransaction finished: sku={sku} consumed {quantity} unit(s) in a single consume.");
                },
                onError: error => XsollaLogger.Error(Tag, $"FinishTransaction failed: sku={sku} quantity={quantity} transactionId={transactionId}: {error}")
            );
        }

        public void RestoreTransactions(Action<bool> onSuccess, Action<string> onError)
        {
            XsollaLogger.Debug(Tag, "RestoreTransactions");
            
            _storeClient.RestorePurchases(
                onSuccess: items =>
                {
                    foreach (var item in items)
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