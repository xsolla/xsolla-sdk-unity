using Xsolla.SDK.Common;

namespace Xsolla.SDK.Store
{
    internal interface IXsollaStoreClient
    {
        void Initialize(XsollaClientConfiguration settings, 
            InitializeResultFunc onSuccess, ErrorFunc onError,
            PurchaseProductResultFunc onSuccessPurchaseProduct, ErrorFunc onErrorPurchase);
        void Deinitialize(DeinitializeResultFunc onSuccess, ErrorFunc onError);
        
        void RestorePurchases(RestorePurchasesResultFunc onSuccess, ErrorFunc onError);
        void FetchProducts(string[] productIds, FetchProductsResultFunc onSuccess, ErrorFunc onError);
        void PurchaseProduct(string sku, string developerPayload, XsollaStoreClientPurchaseArgs args, PurchaseProductResultFunc onSuccess, ErrorFunc onError);

        /// <summary>
        /// Whether a <see cref="PurchaseProduct"/> call with these arguments, made right now, can be answered with the
        /// purchase's result through its callbacks.
        /// <para/>
        /// False for a purchase only a later restore can report, such as a web shop purchase the platform reports no
        /// result of, even when a failure to start it is still reported through <c>onError</c>. The purchasing core
        /// holds nothing back from restores for such a purchase while it waits for an answer.
        /// </summary>
        /// <param name="sku">SKU of the product to purchase.</param>
        /// <param name="args">Purchase arguments.</param>
        bool CanAnswerPurchase(string sku, XsollaStoreClientPurchaseArgs args);
        void ConsumeProduct(string sku, int quantity, string transactionId, ConsumeProductResultFunc onSuccess, ErrorFunc onError);
        void ValidatePurchase(string receipt, ValidatePurchaseResultFunc onSuccess, ErrorFunc onError);
        
        void GetAccessToken(GetAccessTokenResultFunc onSuccess, ErrorFunc onError);
        void UpdateAccessToken(string token, UpdateAccessTokenResultFunc onSuccess, ErrorFunc onError);
        void GetAppleStorefront(GetAppleStorefrontResultFunc onSuccess, ErrorFunc onError);
    }
}