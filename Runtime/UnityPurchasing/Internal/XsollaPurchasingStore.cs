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
    internal class XsollaPurchasingStore : AbstractStore, IXsollaPurchasingStoreConfiguration, IXsollaPurchasingStoreExtension, IXsollaPurchasingCoreCallback
    {
        public const string Name = "XsollaStore";
        
        private const string Tag = "XsollaPurchasingStore";
        
        [CanBeNull] private IStoreCallback _storeCallback;
        private readonly IXsollaStoreClient _storeClient;

        private readonly XsollaPurchasingCore _core;

        [CanBeNull] private Action<IStoreCallback, string, string, string> onPurchaseSucceeded;
        [CanBeNull] private Action<IStoreCallback, PurchaseFailureDescription> onPurchaseFailed;
        
        [CanBeNull] private XsollaPurchasingStoreValidator _validator;

        /// <summary>
        /// Set when <see cref="Initialize"/> threw, until the first catalog fetch is reported.
        /// </summary>
        private bool _initializeThrew;

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
            _core = new XsollaPurchasingCore(
                configuration,
                storeClient,
                this,
                new XsollaPurchasingCoreOptions(
                    restoresWithFirstFetch: true,
                    dropsPurchasesBeforeFirstFetch: true,
                    limitsIdentitylessRestores: false,
                    restoresNonConsumablesUnderNativeIds: true
                ),
                nonConsumableTransactionIds
            );
        }

        /// <summary>
        /// Initializes the core without letting an exception it throws reach Unity IAP, which instead learns of the
        /// failure from the following <see cref="RetrieveProducts"/>.
        /// <para/>
        /// Unity IAP 4.x calls <c>RetrieveProducts</c> only after this returns, and only its answer initializes Unity
        /// IAP or fails it, so a rethrown exception would leave the app's listener answered neither way. The core fails
        /// every catalog fetch after a store client that threw before answering.
        /// </summary>
        public override void Initialize(IStoreCallback callback)
        {
            _storeCallback = callback;

            try
            {
                _core.Initialize();
            }
            catch (Exception e)
            {
                XsollaLogger.Error(Tag, $"Initialize threw, reported to Unity IAP by the first catalog fetch: {e}");
                _initializeThrew = true;
            }
        }

        /// <summary>
        /// Fetches the catalog with each product's type, which the core needs to restore a non-consumable under the id
        /// the Android SDK made up for it, the one Unity IAP's transaction log holds from earlier launches.
        /// </summary>
        public override void RetrieveProducts(ReadOnlyCollection<ProductDefinition> products)
        {
            var productTypes = new Dictionary<string, XsollaPurchasingProductType>(products.Count);
            foreach (var product in products)
                productTypes[product.storeSpecificId] = MapToPurchasingType(product.type);

            _core.FetchProducts(products.MapToArray(product => product.storeSpecificId), productTypes);
        }

        /// <summary>
        /// Ignored: catalog fetches wait for the resolved configuration, not for the client's own result.
        /// </summary>
        void IXsollaPurchasingCoreCallback.OnInitialized() { }

        /// <summary>
        /// Ignored: catalog fetches go ahead after a failure the store client reports, so it is only logged; one it
        /// throws reaches Unity IAP through the first catalog fetch, see <see cref="Initialize"/>.
        /// </summary>
        void IXsollaPurchasingCoreCallback.OnInitializeFailed(string message) { }

        void IXsollaPurchasingCoreCallback.OnProductsFetched(IReadOnlyList<XsollaPurchasingProduct> products)
        {
            _initializeThrew = false;

            var descriptions = new List<ProductDescription>(products.Count);
            foreach (var product in products)
            {
                var metadata = new ProductMetadata(
                    priceString: product.priceString,
                    title: product.title,
                    description: product.description,
                    currencyCode: product.currencyCode,
                    localizedPrice: product.price
                );
                var restoredPurchase = product.restoredPurchase;

                if (restoredPurchase == null)
                {
                    descriptions.Add(new ProductDescription(id: product.sku, metadata: metadata));
                }
                else
                {
                    descriptions.Add(new ProductDescription(
                        id: product.sku,
                        metadata: metadata,
                        receipt: restoredPurchase.receipt,
                        transactionId: restoredPurchase.transactionId
                    ));
                }
            }

            _storeCallback?.OnProductsRetrieved(descriptions);
        }

        /// <summary>
        /// Reported as an empty catalog rather than through <c>OnSetupFailed</c>, as this store always has, except for
        /// the first fetch after <see cref="Initialize"/> threw.
        /// <para/>
        /// That fetch fails with the initialization failure, which Unity IAP 4.x never received before. It is reported
        /// like Unity's own stores report a store they cannot reach, as
        /// <see cref="InitializationFailureReason.PurchasingUnavailable"/> with its message, so the app's listener gets
        /// the cause instead of <see cref="InitializationFailureReason.NoProductsAvailable"/>.
        /// </summary>
        void IXsollaPurchasingCoreCallback.OnProductsFetchFailed(string message)
        {
            if (_initializeThrew)
            {
                _initializeThrew = false;
                _storeCallback?.OnSetupFailed(InitializationFailureReason.PurchasingUnavailable, message);
            }
            else
            {
                _storeCallback?.OnProductsRetrieved(new List<ProductDescription>());
            }
        }

        /// <summary>
        /// Ignored: Unity IAP 4.x completes purchases that exist at startup from the receipts on the retrieved
        /// products, and its <c>OnAllPurchasesRetrieved</c> would clear the receipt of every product missing from the list.
        /// </summary>
        void IXsollaPurchasingCoreCallback.OnPurchasesFetched(IReadOnlyList<XsollaPurchasingPurchase> purchases) { }

        /// <summary>
        /// Ignored, like <see cref="IXsollaPurchasingCoreCallback.OnPurchasesFetched"/>.
        /// </summary>
        void IXsollaPurchasingCoreCallback.OnPurchasesFetchFailed(string message) { }

        void IXsollaPurchasingCoreCallback.OnPurchaseSucceeded(XsollaPurchasingPurchase purchase)
        {
            if (onPurchaseSucceeded != null)
                onPurchaseSucceeded(_storeCallback, purchase.sku, purchase.receipt, purchase.transactionId);
            else
                _storeCallback?.OnPurchaseSucceeded(
                    storeSpecificId: purchase.sku,
                    receipt: purchase.receipt,
                    transactionIdentifier: purchase.transactionId
                );
        }

        void IXsollaPurchasingCoreCallback.OnPurchaseFailed(XsollaPurchasingFailure failure)
        {
            var desc = new PurchaseFailureDescription(
                productId: failure.sku,
                reason: MapToIapReason(failure.reason),
                message: failure.message
            );

            if (onPurchaseFailed != null)
                onPurchaseFailed(_storeCallback, desc);
            else
                _storeCallback?.OnPurchaseFailed(desc);
        }

        public override void Purchase(ProductDefinition product, string developerPayload) =>
            _core.Purchase(product.storeSpecificId, developerPayload, XsollaStoreClientPurchaseArgs.Empty);

        /// <summary>
        /// Reports every reason but a cancellation as <see cref="PurchaseFailureReason.PaymentDeclined"/>, as this store
        /// always has: Unity IAP 4.x integrators may switch on it.
        /// </summary>
        private static PurchaseFailureReason MapToIapReason(XsollaPurchasingFailureReason reason)
        {
            switch (reason)
            {
                case XsollaPurchasingFailureReason.UserCancelled:
                    return PurchaseFailureReason.UserCancelled;
                default:
                    return PurchaseFailureReason.PaymentDeclined;
            }
        }

        private static XsollaPurchasingProductType MapToPurchasingType(ProductType type)
        {
            switch (type)
            {
                case ProductType.Consumable:
                    return XsollaPurchasingProductType.Consumable;
                case ProductType.Subscription:
                    return XsollaPurchasingProductType.Subscription;
                default:
                    return XsollaPurchasingProductType.NonConsumable;
            }
        }

        /// <summary>
        /// Consumes the purchase through the core, which drops every repeated finish of a purchase it is consuming or
        /// has consumed.
        /// <para/>
        /// Unity IAP finishes the same purchase more than once: its <c>ProcessPurchaseOnStart</c> walks the whole
        /// product catalog on every <c>OnProductsRetrieved</c> and re-finishes every transaction already in its
        /// log, with no <c>ProcessPurchase</c> in between — so any later <c>FetchAdditionalProducts</c> re-finishes
        /// an earlier restored purchase. Unity IAP 4.x takes no consume result, so none is requested.
        /// </summary>
        public override void FinishTransaction(ProductDefinition product, string transactionId) =>
            _core.Consume(
                product.storeSpecificId,
                transactionId,
                MapToPurchasingType(product.type),
                onSuccess: null,
                onFailed: null
            );

        public void RestoreTransactions(Action<bool> onSuccess, Action<string> onError) =>
            _core.RestoreTransactions(onSuccess, onError);

        public bool TryGetProductIconUrl(Product product, out string url)
        {
            var res = _core.TryGetProduct(product.definition.storeSpecificId, out var productData);
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
            return _core.TryGetProduct(product.definition.storeSpecificId, out productData);
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

        public void GetAccessToken(Action<string> onSuccess, Action<string> onError) =>
            _core.GetAccessToken(onSuccess, onError);

        public void UpdateAccessToken(string token, Action onSuccess, Action<string> onError) =>
            _core.UpdateAccessToken(token, onSuccess, onError);

        public void GetAppleStorefront(Action<string> onSuccess, Action<string> onError) =>
            _core.GetAppleStorefront(onSuccess, onError);

        public void InitiatePurchase(Product product, XsollaStoreClientPurchaseArgs args) =>
            _core.InitiatePurchase(product.definition.storeSpecificId, args);

        public void InitiatePurchase(string productId, XsollaStoreClientPurchaseArgs args) =>
            _core.InitiatePurchase(productId, args);

    }
}
#endif