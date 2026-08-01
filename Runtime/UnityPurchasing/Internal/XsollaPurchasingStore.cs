#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
using Xsolla.SDK.Common;
using Xsolla.SDK.Store;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.UnityPurchasing
{
    /// <summary>
    /// Unity IAP 5 store implementation backed by the Xsolla Store client.
    /// </summary>
    internal sealed class XsollaPurchasingStore : UnityEngine.Purchasing.Extension.Store
    {
        public const string Name = "XsollaStore";

        private const string Tag = "XsollaPurchasingStore";

        private readonly IXsollaStoreClient _storeClient = XsollaStoreClientFactory.Create();
        private readonly ISimpleFuture<XsollaClientConfiguration, string> _settingsFuture;
        private readonly Dictionary<string, ProductDefinition> _definitionBySku = new Dictionary<string, ProductDefinition>();
        private readonly Dictionary<string, XsollaStoreClientProduct> _productById = new Dictionary<string, XsollaStoreClientProduct>();
        private readonly Dictionary<string, Queue<ICart>> _pendingCartsBySku = new Dictionary<string, Queue<ICart>>();
        private readonly Dictionary<string, int> _quantityByTransactionId = new Dictionary<string, int>();
        private readonly HashSet<string> _reportedTransactionIds = new HashSet<string>();
        private readonly List<XsollaStoreClientPurchasedProduct> _unreportedPurchases = new List<XsollaStoreClientPurchasedProduct>();

        private XsollaPurchasingStoreValidator _validator;

        internal ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;

        internal XsollaPurchasingStore(XsollaClientConfiguration configuration)
        {
            RunOnStartThread.Create();

            _settingsFuture = SimpleFuture.Create<XsollaClientConfiguration, string>(out var promise);
            XsollaLogger.SetLogLevel(configuration.logLevel);

            if (configuration.delayedTask != null)
                AwaitForConfiguration(configuration, promise);
            else
                promise.Complete(configuration);
        }

        private async void AwaitForConfiguration(
            XsollaClientConfiguration configuration,
            ISimplePromise<XsollaClientConfiguration, string> promise)
        {
            try
            {
                var mapper = await configuration.delayedTask;
                RunOnStartThread.Run(() => promise.Complete(mapper(configuration)));
            }
            catch (Exception exception)
            {
                RunOnStartThread.Run(() => promise.CompleteWithError(exception.Message));
            }
        }

        public override void Connect()
        {
            XsollaLogger.Debug(Tag, "Connect");

            if (ConnectionState == ConnectionState.Connected)
            {
                ConnectCallback?.OnStoreConnectionSucceeded();
                return;
            }

            ConnectionState = ConnectionState.Connecting;
            _settingsFuture.OnComplete(
                onSuccess: configuration =>
                {
                    XsollaLogger.SetLogLevel(configuration.logLevel);
                    _storeClient.Initialize(
                        configuration,
                        onSuccess: () =>
                        {
                            ConnectionState = ConnectionState.Connected;
                            XsollaLogger.Debug(Tag, "Connect finished");
                            ConnectCallback?.OnStoreConnectionSucceeded();
                        },
                        onError: ReportConnectionFailure,
                        onSuccessPurchaseProduct: OnPurchaseSucceeded,
                        onErrorPurchase: error => OnPurchaseFailed(error, null));
                },
                onError: ReportConnectionFailure);
        }

        private void ReportConnectionFailure(string error)
        {
            ConnectionState = ConnectionState.Disconnected;
            XsollaLogger.Error(Tag, $"Connect failed: {error}");
            ConnectCallback?.OnStoreConnectionFailed(new StoreConnectionFailureDescription(error, true));
        }

        public override void FetchProducts(IReadOnlyCollection<ProductDefinition> products)
        {
            XsollaLogger.Debug(Tag, "FetchProducts");

            if (ConnectionState != ConnectionState.Connected)
            {
                ProductsCallback?.OnProductsFetchFailed(new ProductFetchFailureDescription(
                    ProductFetchFailureReason.ProviderUnavailable,
                    "Xsolla store is not connected.",
                    true));
                return;
            }

            foreach (var definition in products)
                _definitionBySku[definition.storeSpecificId] = definition;

            _storeClient.FetchProducts(
                products.Select(product => product.storeSpecificId).ToArray(),
                onSuccess: items =>
                {
                    var descriptions = new List<ProductDescription>(items.Length);
                    foreach (var item in items)
                    {
                        _productById[item.sku] = item;
                        var localizedPrice = (decimal)item.localizedPrice / 1_000_000;
                        var metadata = new ProductMetadata(
                            item.localizedPriceString,
                            item.localizedTitle,
                            item.localizedDescription,
                            item.currencyCode,
                            localizedPrice);

                        var type = _definitionBySku.TryGetValue(item.sku, out var definition)
                            ? definition.type
                            : ProductType.Unknown;
                        descriptions.Add(new ProductDescription(item.sku, metadata, null, null, type));
                    }

                    XsollaLogger.Debug(Tag, $"FetchProducts finished: {descriptions.Count} product(s)");
                    ProductsCallback?.OnProductsFetched(descriptions);
                    FlushUnreportedPurchases();
                },
                onError: error =>
                {
                    XsollaLogger.Error(Tag, $"FetchProducts failed: {error}");
                    ProductsCallback?.OnProductsFetchFailed(new ProductFetchFailureDescription(
                        ProductFetchFailureReason.Unknown,
                        error,
                        true));
                });
        }

        public override void FetchPurchases()
        {
            FetchPurchasesInternal(null);
        }

        internal void RestoreTransactions(Action<bool, string> callback)
        {
            FetchPurchasesInternal(callback);
        }

        private void FetchPurchasesInternal(Action<bool, string> completionHandler)
        {
            XsollaLogger.Debug(Tag, "FetchPurchases");

            if (ConnectionState != ConnectionState.Connected)
            {
                const string error = "Xsolla store is not connected.";
                PurchaseFetchCallback?.OnPurchasesRetrievalFailed(new PurchasesFetchFailureDescription(
                    PurchasesFetchFailureReason.StoreNotConnected,
                    error));
                completionHandler?.Invoke(false, error);
                return;
            }

            _storeClient.RestorePurchases(
                onSuccess: items =>
                {
                    var orders = BuildRestoredOrders(items);
                    XsollaLogger.Debug(Tag, $"FetchPurchases finished: {orders.Count} order(s)");
                    PurchaseFetchCallback?.OnAllPurchasesRetrieved(orders);
                    completionHandler?.Invoke(true, null);
                },
                onError: error =>
                {
                    XsollaLogger.Error(Tag, $"FetchPurchases failed: {error}");
                    PurchaseFetchCallback?.OnPurchasesRetrievalFailed(new PurchasesFetchFailureDescription(
                        PurchasesFetchFailureReason.Unknown,
                        error));
                    completionHandler?.Invoke(false, error);
                });
        }

        private List<Order> BuildRestoredOrders(IEnumerable<XsollaStoreClientPurchasedProduct> purchases)
        {
            var orders = new List<Order>();
            foreach (var purchase in purchases)
            {
                var product = FindProduct(purchase.sku);
                if (product == null)
                {
                    XsollaLogger.Warning(Tag, $"Ignoring restored purchase for unknown product '{purchase.sku}'. Fetch products before fetching purchases.");
                    continue;
                }

                TrackQuantity(purchase);
                TrackReportedTransaction(purchase.transactionId);
                var cart = new Cart(product);
                var info = new XsollaOrderInfo(purchase.ToReceipt().ToJson(), purchase.transactionId);

                if (product.definition.type == ProductType.Consumable)
                    orders.Add(new PendingOrder(cart, info));
                else
                    orders.Add(new ConfirmedOrder(cart, info));
            }

            return orders;
        }

        public override void Purchase(ICart cart)
        {
            var items = cart?.Items();
            if (items == null || items.Count != 1)
            {
                if (cart != null)
                {
                    PurchaseCallback?.OnPurchaseFailed(new FailedOrder(
                        cart,
                        PurchaseFailureReason.ProductUnavailable,
                        "Xsolla purchases must contain exactly one product."));
                }
                return;
            }

            Purchase(items[0].Product, cart, null, XsollaStoreClientPurchaseArgs.Empty);
        }

        internal void InitiatePurchase(Product product, XsollaStoreClientPurchaseArgs args)
        {
            if (product == null)
                return;

            var cart = new Cart(product);
            Purchase(product, cart, null, args ?? XsollaStoreClientPurchaseArgs.Empty);
        }

        internal void InitiatePurchase(string productId, XsollaStoreClientPurchaseArgs args)
        {
            var product = FindProduct(productId);
            if (product == null)
            {
                XsollaLogger.Error(Tag, $"InitiatePurchase failed: product '{productId}' has not been fetched.");
                return;
            }

            InitiatePurchase(product, args);
        }

        private void Purchase(Product product, ICart cart, string developerPayload, XsollaStoreClientPurchaseArgs args)
        {
            var sku = product.definition.storeSpecificId;
            EnqueueCart(sku, cart);
            XsollaLogger.Debug(Tag, $"Purchase: {sku}");

            _storeClient.PurchaseProduct(
                sku,
                developerPayload,
                args,
                onSuccess: OnPurchaseSucceeded,
                onError: error => OnPurchaseFailed(error, sku));
        }

        private void OnPurchaseSucceeded(XsollaStoreClientPurchasedProduct purchase)
        {
            XsollaLogger.Debug(Tag, $"Purchase finished: {purchase}");
            if (!TryReportPurchase(purchase))
                _unreportedPurchases.Add(purchase);
        }

        private bool TryReportPurchase(XsollaStoreClientPurchasedProduct purchase)
        {
            if (!string.IsNullOrEmpty(purchase.transactionId) && _reportedTransactionIds.Contains(purchase.transactionId))
                return true;

            if (PurchaseCallback == null || !TryGetCart(purchase.sku, true, out var cart))
                return false;

            TrackQuantity(purchase);
            var order = new PendingOrder(
                cart,
                new XsollaOrderInfo(purchase.ToReceipt().ToJson(), purchase.transactionId));
            PurchaseCallback.OnPurchaseSucceeded(order);
            TrackReportedTransaction(purchase.transactionId);
            return true;
        }

        private void FlushUnreportedPurchases()
        {
            for (var index = _unreportedPurchases.Count - 1; index >= 0; index--)
            {
                if (!TryReportPurchase(_unreportedPurchases[index]))
                    continue;

                _unreportedPurchases.RemoveAt(index);
            }
        }

        private void OnPurchaseFailed(string error, string sku)
        {
            var parsed = XsollaStoreClientHelpers.ParsePurchaseError(error);
            var reason = MapToIapReason(parsed.code);
            if (reason == PurchaseFailureReason.UserCancelled)
                XsollaLogger.Warning(Tag, $"Purchase failed: {parsed.message}, reason: {reason}, sku: {sku}");
            else
                XsollaLogger.Error(Tag, $"Purchase failed: {parsed.message}, reason: {reason}, sku: {sku}");

            if (string.IsNullOrEmpty(sku) || PurchaseCallback == null || !TryGetCart(sku, true, out var cart))
                return;

            PurchaseCallback.OnPurchaseFailed(new FailedOrder(cart, reason, parsed.message));
        }

        private static PurchaseFailureReason MapToIapReason(XsollaStoreClientPurchaseErrorCode code)
        {
            return code == XsollaStoreClientPurchaseErrorCode.Cancelled
                ? PurchaseFailureReason.UserCancelled
                : PurchaseFailureReason.PaymentDeclined;
        }

        public override void FinishTransaction(PendingOrder pendingOrder)
        {
            var item = pendingOrder?.CartOrdered?.Items().FirstOrDefault();
            if (item == null)
            {
                if (pendingOrder != null)
                    ConfirmCallback?.OnConfirmOrderFailed(new FailedOrder(pendingOrder, PurchaseFailureReason.Unknown, "Pending order has no product."));
                return;
            }

            var product = item.Product;
            var sku = product.definition.storeSpecificId;
            var transactionId = pendingOrder.Info.TransactionID;

            if (product.definition.type != ProductType.Consumable)
            {
                _quantityByTransactionId.Remove(transactionId);
                ConfirmCallback?.OnConfirmOrderSucceeded(transactionId);
                return;
            }

            var quantity = _quantityByTransactionId.TryGetValue(transactionId, out var trackedQuantity)
                ? trackedQuantity
                : 1;

            XsollaLogger.Debug(Tag, $"FinishTransaction: consuming sku={sku} quantity={quantity} transactionId={transactionId}");
            _storeClient.ConsumeProduct(
                sku,
                quantity,
                transactionId,
                onSuccess: () =>
                {
                    _quantityByTransactionId.Remove(transactionId);
                    XsollaLogger.Debug(Tag, $"FinishTransaction finished: sku={sku} quantity={quantity}");
                    ConfirmCallback?.OnConfirmOrderSucceeded(transactionId);
                },
                onError: error =>
                {
                    XsollaLogger.Error(Tag, $"FinishTransaction failed: sku={sku} quantity={quantity} transactionId={transactionId}: {error}");
                    ConfirmCallback?.OnConfirmOrderFailed(new FailedOrder(pendingOrder, PurchaseFailureReason.Unknown, error));
                });
        }

        public override void CheckEntitlement(ProductDefinition product)
        {
            if (product == null)
                return;

            _storeClient.RestorePurchases(
                onSuccess: purchases =>
                {
                    var entitled = purchases.Any(item => item.sku == product.storeSpecificId);
                    var status = !entitled
                        ? EntitlementStatus.NotEntitled
                        : product.type == ProductType.Consumable
                            ? EntitlementStatus.EntitledUntilConsumed
                            : EntitlementStatus.FullyEntitled;
                    EntitlementCallback?.OnCheckEntitlement(product, status);
                },
                onError: error => EntitlementCallback?.OnCheckEntitlement(product, EntitlementStatus.Unknown, error));
        }

        private void TrackQuantity(XsollaStoreClientPurchasedProduct purchase)
        {
            if (!string.IsNullOrEmpty(purchase.transactionId))
                _quantityByTransactionId[purchase.transactionId] = purchase.quantity > 0 ? purchase.quantity : 1;
        }

        private void TrackReportedTransaction(string transactionId)
        {
            if (!string.IsNullOrEmpty(transactionId))
                _reportedTransactionIds.Add(transactionId);
        }

        private void EnqueueCart(string sku, ICart cart)
        {
            if (!_pendingCartsBySku.TryGetValue(sku, out var carts))
            {
                carts = new Queue<ICart>();
                _pendingCartsBySku[sku] = carts;
            }

            carts.Enqueue(cart);
        }

        private bool TryGetCart(string sku, bool consumeQueuedCart, out ICart cart)
        {
            if (_pendingCartsBySku.TryGetValue(sku, out var carts) && carts.Count > 0)
            {
                cart = consumeQueuedCart ? carts.Dequeue() : carts.Peek();
                if (carts.Count == 0)
                    _pendingCartsBySku.Remove(sku);
                return true;
            }

            var product = FindProduct(sku);
            if (product != null)
            {
                cart = new Cart(product);
                return true;
            }

            cart = null;
            return false;
        }

        private static Product FindProduct(string productId)
        {
            try
            {
                return UnityIAPServices.Product(Name).GetProductById(productId);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal bool TryGetProductIconUrl(Product product, out string url)
        {
            if (product != null && _productById.TryGetValue(product.definition.storeSpecificId, out var productData))
            {
                url = productData.iconUrl;
                return true;
            }

            url = null;
            return false;
        }

        internal bool TryGetProduct(Product product, out XsollaStoreClientProduct productData)
        {
            if (product != null)
                return _productById.TryGetValue(product.definition.storeSpecificId, out productData);

            productData = null;
            return false;
        }

        internal XsollaPurchasingStoreValidator GetValidator()
        {
            return _validator ?? (_validator = new XsollaPurchasingStoreValidator(_storeClient));
        }

        internal void GetAccessToken(Action<string> onSuccess, Action<string> onError)
        {
            _storeClient.GetAccessToken(
                token => onSuccess?.Invoke(token),
                error => onError?.Invoke(error));
        }

        internal void UpdateAccessToken(string token, Action onSuccess, Action<string> onError)
        {
            _storeClient.UpdateAccessToken(
                token,
                () => onSuccess?.Invoke(),
                error => onError?.Invoke(error));
        }

        internal void GetAppleStorefront(Action<string> onSuccess, Action<string> onError)
        {
            _storeClient.GetAppleStorefront(
                storefront => onSuccess?.Invoke(storefront),
                error => onError?.Invoke(error));
        }
    }
}
#endif
