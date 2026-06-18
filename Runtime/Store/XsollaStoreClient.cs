using System;
using System.Collections.Generic;
using Xsolla.SDK.Common;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// Provides store client functionality for Xsolla SDK.
    /// </summary>
    public class XsollaStoreClient
    {
        private readonly IXsollaStoreClient _storeImpl = XsollaStoreClientFactory.Create();
        private readonly List<string> _products = new List<string>();
        private readonly Action<XsollaStoreClientPurchasedProduct, XsollaStoreClientError> _onRestore;
        private readonly ISimpleFuture<XsollaClientConfiguration, string> _settingsFuture;

        /// <summary>
        /// Builder for <see cref="XsollaStoreClient"/>.
        /// </summary>
        public class Builder
        {
            private XsollaClientConfiguration _configuration;
            private Action<XsollaStoreClientPurchasedProduct, XsollaStoreClientError> _onRestore;
            private readonly List<string> _products = new List<string>();

            /// <summary>
            /// Creates a new builder instance.
            /// </summary>
            public static Builder Create() => new Builder();

            /// <summary>
            /// Sets the client configuration.
            /// </summary>
            /// <param name="configuration">Client configuration.</param>
            public Builder SetConfiguration(XsollaClientConfiguration configuration) { _configuration = configuration; return this; }

            /// <summary>
            /// Sets the restore callback.
            /// </summary>
            /// <param name="onRestore">Callback for restored purchases.</param>
            public Builder SetOnRestore(Action<XsollaStoreClientPurchasedProduct, XsollaStoreClientError> onRestore) { _onRestore = onRestore; return this; }

            /// <summary>
            /// Adds a product SKU.
            /// </summary>
            /// <param name="product">Product SKU.</param>
            public Builder AddProduct(string product) { _products.Add(product); return this; }

            /// <summary>
            /// Adds multiple product SKUs.
            /// </summary>
            /// <param name="products">Array of product SKUs.</param>
            public Builder AddProducts(string[] products) { _products.AddRange(products); return this; }

            /// <summary>
            /// Builds the <see cref="XsollaStoreClient"/> instance.
            /// </summary>
            public XsollaStoreClient Build() => new XsollaStoreClient(_configuration, _products.ToArray(), _onRestore);
        }

        /// <summary>
        /// Initializes a new instance of <see cref="XsollaStoreClient"/>.
        /// </summary>
        /// <param name="configuration">Client configuration.</param>
        /// <param name="products">Array of product SKUs.</param>
        /// <param name="onRestore">Callback for restored purchases.</param>
        private XsollaStoreClient(XsollaClientConfiguration configuration, string[] products, Action<XsollaStoreClientPurchasedProduct, XsollaStoreClientError> onRestore)
        {
            _products.AddRange(products);
            _onRestore = onRestore;

            RunOnStartThread.Create();
            XsollaLogger.SetLogLevel(configuration.logLevel);

            _settingsFuture = SimpleFuture.Create<XsollaClientConfiguration, string>(out var promise);
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

        /// <summary>
        /// Initializes the store client and fetches products.
        /// </summary>
        /// <param name="completionHandler">Callback with products array and error.</param>
        public void Initialize(Action<XsollaStoreClientProduct[], XsollaStoreClientError> completionHandler) =>
            _settingsFuture.OnComplete(
                onError: error => completionHandler?.Invoke(null, XsollaStoreClientError.Message(error)),
                onSuccess: configuration => Initialize_(configuration, completionHandler)
            );

        /// <summary>
        /// Internal initialization logic.
        /// </summary>
        /// <param name="configuration">Client configuration.</param>
        /// <param name="completionHandler">Callback with products array and error.</param>
        private void Initialize_(XsollaClientConfiguration configuration,
            Action<XsollaStoreClientProduct[], XsollaStoreClientError> completionHandler)
        {
            XsollaLogger.SetLogLevel(configuration.logLevel);

            _storeImpl.Initialize(
                configuration,
                onSuccess: () =>
                {
                    _storeImpl.FetchProducts(_products.ToArray(),
                        onSuccess: items =>
                        {
                            completionHandler?.Invoke(items, null);

#if !UNITY_IOS
                            // iOS restores automatically on start via the native SDK observer
                            _storeImpl.RestorePurchases(
                                onSuccess: restored =>
                                {
                                    foreach (var item in restored)
                                        _onRestore?.Invoke(item, null);
                                },
                                onError: error => _onRestore?.Invoke(null, XsollaStoreClientHelpers.ParsePurchaseError(error))
                            );
#endif
                        },
                        onError: error => completionHandler?.Invoke(null, XsollaStoreClientError.Message(error))
                    );
                },
                onError: error => completionHandler?.Invoke(null, XsollaStoreClientHelpers.ParsePurchaseError(error)),
                onSuccessPurchaseProduct: item => _onRestore?.Invoke(item, null),
                onErrorPurchase: error => _onRestore?.Invoke(null, XsollaStoreClientHelpers.ParsePurchaseError(error))
            );
        }

        /// <summary>
        /// Fetches additional products, skipping any that have already been loaded.
        /// </summary>
        /// <param name="products">Array of product SKUs to fetch.</param>
        /// <param name="completionHandler">Callback with the fetched products array and error. Returns an empty array when there are no new products to fetch.</param>
        public void FetchAdditionalProducts(string[] products, Action<XsollaStoreClientProduct[], XsollaStoreClientError> completionHandler)
        {
            var newProducts = new List<string>();
            if (products != null)
            {
                foreach (var product in products)
                {
                    if (_products.Contains(product))
                        continue;

                    newProducts.Add(product);
                }
            }

            if (newProducts.Count == 0)
            {
                completionHandler?.Invoke(new XsollaStoreClientProduct[0], null);
                return;
            }

            _storeImpl.FetchProducts(newProducts.ToArray(),
                onSuccess: items =>
                {
                    _products.AddRange(newProducts);
                    completionHandler?.Invoke(items, null);
                },
                onError: error => completionHandler?.Invoke(null, XsollaStoreClientError.Message(error))
            );
        }

        /// <summary>
        /// Purchases a product by ID.
        /// </summary>
        /// <param name="productId">Product ID (SKU).</param>
        /// <param name="completionHandler">Callback with purchased product and error.</param>
        public void PurchaseProduct(string productId, Action<XsollaStoreClientPurchasedProduct, XsollaStoreClientError> completionHandler) =>
            _storeImpl.PurchaseProduct(productId, null, XsollaStoreClientPurchaseArgs.Empty,
                onSuccess: item => completionHandler?.Invoke(item, null),
                onError: error => completionHandler?.Invoke(null, XsollaStoreClientHelpers.ParsePurchaseError(error))
            );

        /// <summary>
        /// Purchases a product by ID with developer payload.
        /// </summary>
        /// <param name="productId">Product ID (SKU).</param>
        /// <param name="developerPayload">Developer payload string.</param>
        /// <param name="completionHandler">Callback with purchased product and error.</param>
        public void PurchaseProduct(string productId, string developerPayload, Action<XsollaStoreClientPurchasedProduct, XsollaStoreClientError> completionHandler) =>
            _storeImpl.PurchaseProduct(productId, developerPayload, XsollaStoreClientPurchaseArgs.Empty,
                onSuccess: item => completionHandler?.Invoke(item, null),
                onError: error => completionHandler?.Invoke(null, XsollaStoreClientHelpers.ParsePurchaseError(error))
            );

        /// <summary>
        /// Purchases a product by ID with purchase arguments.
        /// </summary>
        /// <param name="productId">Product ID (SKU).</param>
        /// <param name="args">Purchase arguments.</param>
        /// <param name="completionHandler">Callback with purchased product and error.</param>
        public void PurchaseProduct(string productId, XsollaStoreClientPurchaseArgs args, Action<XsollaStoreClientPurchasedProduct, XsollaStoreClientError> completionHandler) =>
            _storeImpl.PurchaseProduct(productId, null, args,
                onSuccess: item => completionHandler?.Invoke(item, null),
                onError: error => completionHandler?.Invoke(null, XsollaStoreClientHelpers.ParsePurchaseError(error))
            );

        /// <summary>
        /// Purchases a product by ID with developer payload and purchase arguments.
        /// </summary>
        /// <param name="productId">Product ID (SKU).</param>
        /// <param name="developerPayload">Developer payload string.</param>
        /// <param name="args">Purchase arguments.</param>
        /// <param name="completionHandler">Callback with purchased product and error.</param>
        public void PurchaseProduct(string productId, string developerPayload, XsollaStoreClientPurchaseArgs args, Action<XsollaStoreClientPurchasedProduct, XsollaStoreClientError> completionHandler) =>
            _storeImpl.PurchaseProduct(productId, developerPayload, args,
                onSuccess: item => completionHandler?.Invoke(item, null),
                onError: error => completionHandler?.Invoke(null, XsollaStoreClientHelpers.ParsePurchaseError(error))
            );
        
        /// <summary>
        /// Purchases a product by ID with purchase arguments.
        /// </summary>
        /// <param name="args">Purchase arguments.</param>
        /// <param name="completionHandler">Callback with purchased product and error.</param>
        public void PurchaseProduct(XsollaStoreClientPurchaseArgs args, Action<XsollaStoreClientPurchasedProduct, XsollaStoreClientError> completionHandler) =>
            _storeImpl.PurchaseProduct(null, null, args,
                onSuccess: item => completionHandler?.Invoke(item, null),
                onError: error => completionHandler?.Invoke(null, XsollaStoreClientHelpers.ParsePurchaseError(error))
            );

        /// <summary>
        /// Consumes a purchased product.
        /// </summary>
        /// <param name="sku">Product SKU.</param>
        /// <param name="quantity">Quantity to consume.</param>
        /// <param name="transactionId">Transaction ID.</param>
        /// <param name="completionHandler">Callback with error.</param>
        public void ConsumeProduct(string sku, int quantity, string transactionId, Action<XsollaStoreClientError> completionHandler) =>
            _storeImpl.ConsumeProduct(sku, quantity, transactionId,
                onSuccess: () => completionHandler?.Invoke(null),
                onError: error => completionHandler?.Invoke(XsollaStoreClientError.Message(error) )
            );

        /// <summary>
        /// Validates a purchase receipt.
        /// </summary>
        /// <param name="receipt">Purchase receipt.</param>
        /// <param name="completionHandler">Callback with validation result and error.</param>
        public void ValidatePurchase(XsollaStoreClientPurchaseReceipt receipt, Action<bool, XsollaStoreClientError> completionHandler) =>
            _storeImpl.ValidatePurchase(receipt.ToJson(),
                onSuccess: result => completionHandler?.Invoke(result, null),
                onError: error => completionHandler?.Invoke(false, XsollaStoreClientError.Message(error))
            );

        /// <summary>
        /// Deinitializes the store client.
        /// </summary>
        /// <param name="onSuccess">Callback on success.</param>
        /// <param name="onError">Callback on error.</param>
        public void Deinitialize(DeinitializeResultFunc onSuccess, ErrorFunc onError) =>
            _storeImpl.Deinitialize(onSuccess, onError);

        /// <summary>
        /// Deinitializes the store client.
        /// </summary>
        /// <param name="completionHandler">Callback with error.</param>
        public void Deinitialize(Action<XsollaStoreClientError> completionHandler) =>
           _storeImpl.Deinitialize(
               onSuccess: () => completionHandler?.Invoke(null), 
               onError: error => completionHandler?.Invoke(XsollaStoreClientHelpers.ParsePurchaseError(error))
           );

        /// <summary>
        /// Gets the access token.
        /// </summary>
        /// <param name="completionHandler">Callback with access token and error.</param>
        public void GetAccessToken(Action<string, XsollaStoreClientError> completionHandler) =>
            _storeImpl.GetAccessToken(
                onSuccess: token => completionHandler?.Invoke(token, null),
                onError: error => completionHandler?.Invoke( null, XsollaStoreClientError.Message(error) )
            );
        
        /// <summary>
        /// Updates the access token for the store client.
        /// </summary>
        /// <param name="token">The new access token.</param>
        /// <param name="completionHandler">Callback with error if the update fails.</param>
        public void UpdateAccessToken(string token, Action<XsollaStoreClientError> completionHandler) =>
            _storeImpl.UpdateAccessToken(
                token,
                onSuccess: () => completionHandler?.Invoke( null ),
                onError: error => completionHandler?.Invoke( XsollaStoreClientError.Message(error) )
            );

        /// <summary>
        /// Gets the Apple Storefront identifier.
        /// </summary>
        /// <param name="completionHandler">Callback with storefront identifier and error.</param>
        public void GetAppleStorefront(Action<string, XsollaStoreClientError> completionHandler) =>
            _storeImpl.GetAppleStorefront(
                onSuccess: storefront => completionHandler?.Invoke(storefront, null),
                onError: error => completionHandler?.Invoke( null, XsollaStoreClientError.Message(error) )
            );
        
        /// <summary>
        /// Restores previous purchases.
        /// </summary>
        /// <param name="completionHandler">Callback with restoration result and error.</param>
        public void RestorePurchases(Action<bool, XsollaStoreClientError> completionHandler)
        {
            _storeImpl.RestorePurchases(
                onSuccess: restored =>
                {
                    foreach (var item in restored)
                        _onRestore?.Invoke(item, null);

                    completionHandler?.Invoke(restored.Length > 0, null);
                },
                onError: error => completionHandler?.Invoke(false, XsollaStoreClientHelpers.ParsePurchaseError(error))
            );
        }

        /// <inheritdoc cref="RestorePurchases"/>
        [Obsolete("Use RestorePurchases instead.")]
        public void RetorePurchases(Action<bool, XsollaStoreClientError> completionHandler) =>
            RestorePurchases(completionHandler);
    }
}