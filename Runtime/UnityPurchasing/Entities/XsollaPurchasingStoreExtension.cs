#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using System;
using UnityEngine.Purchasing;
using Xsolla.SDK.Store;

namespace Xsolla.SDK.UnityPurchasing
{
    /// <summary>
    /// Xsolla-specific operations exposed by the Unity IAP 5 purchase service.
    /// </summary>
    public interface IXsollaPurchasingStoreExtension : IPurchaseServiceExtension
    {
        bool TryGetProductIconUrl(Product product, out string url);
        bool TryGetProduct(Product product, out XsollaStoreClientProduct productData);
        XsollaPurchasingStoreValidator GetValidator();
        void GetAccessToken(Action<string> onSuccess, Action<string> onError);
        void GetAppleStorefront(Action<string> onSuccess, Action<string> onError);
        void InitiatePurchase(Product product, XsollaStoreClientPurchaseArgs args);
        void InitiatePurchase(string productId, XsollaStoreClientPurchaseArgs args);
        void UpdateAccessToken(string token, Action onSuccess, Action<string> onError);
    }
}
#endif
