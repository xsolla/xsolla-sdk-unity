#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using System;
using UnityEngine.Purchasing;
using Xsolla.SDK.Store;

namespace Xsolla.SDK.UnityPurchasing
{
    internal sealed class XsollaPurchasingService : ExtensiblePurchaseService, IXsollaPurchasingStoreExtension
    {
        private readonly XsollaPurchasingStore _store;

        internal XsollaPurchasingService(IPurchaseService basePurchaseService, XsollaPurchasingStore store)
            : base(basePurchaseService)
        {
            _store = store;
        }

        public override void RestoreTransactions(Action<bool, string> callback)
        {
            _store.RestoreTransactions(callback);
        }

        public bool TryGetProductIconUrl(Product product, out string url) =>
            _store.TryGetProductIconUrl(product, out url);

        public bool TryGetProduct(Product product, out XsollaStoreClientProduct productData) =>
            _store.TryGetProduct(product, out productData);

        public XsollaPurchasingStoreValidator GetValidator() => _store.GetValidator();

        public void GetAccessToken(Action<string> onSuccess, Action<string> onError) =>
            _store.GetAccessToken(onSuccess, onError);

        public void GetAppleStorefront(Action<string> onSuccess, Action<string> onError) =>
            _store.GetAppleStorefront(onSuccess, onError);

        public void InitiatePurchase(Product product, XsollaStoreClientPurchaseArgs args) =>
            _store.InitiatePurchase(product, args);

        public void InitiatePurchase(string productId, XsollaStoreClientPurchaseArgs args) =>
            _store.InitiatePurchase(productId, args);

        public void UpdateAccessToken(string token, Action onSuccess, Action<string> onError) =>
            _store.UpdateAccessToken(token, onSuccess, onError);
    }
}
#endif
