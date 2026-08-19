using JetBrains.Annotations;
using Xsolla.SDK.Common;
using Xsolla.SDK.Utils;
using Newtonsoft.Json.Linq;

namespace Xsolla.SDK.Store
{
    internal static class XsollaStoreClientHelpers
    {
        public const string Tag = "XsollaStoreClientHelpers";
        
        public static XsollaStoreClientPurchasedProduct[] JsonToRestoredItems(string json)
        {
            var result = XsollaClientHelpers.FromJson<XsollaStoreClientPurchasedProductResult>(json);
            
            return result.purchases.Map( item => 
                XsollaStoreClientPurchasedProduct.Builder.Create()
                    .FromData(item)
                    .SetStatus(XsollaStoreClientPurchasedProduct.Status.Restored) // mark as restore for future usage
                    .Build() 
            );
        }
        
        public static string ProductIdsToJson(string[] productIds)
        {
            var request = new XsollaStoreClientProductsRequest(productIds);
            return XsollaClientHelpers.ToJson(request);
        }
        
        public static XsollaStoreClientProduct[] JsonToProducts(string json)
        {
            var result = XsollaClientHelpers.FromJson<XsollaStoreClientProductsResult>(json);
            return result.products;
        }
        
        public static XsollaStoreClientPurchasedProduct JsonToPurchase(string json, string developerPayload)
        {
            var result = XsollaClientHelpers.FromJson<XsollaStoreClientPurchasedProductResult>(json);
            return XsollaStoreClientPurchasedProduct.Builder.Create()
                .FromData(result.purchases[0])
                .SetDeveloperPayload(developerPayload)
                .Build();
        }
        
        public static bool JsonToValidate(string json)
        {
            var result = XsollaClientHelpers.FromJson<XsollaStoreClientValidatePurchaseResult>(json);
            return result != null ? result.success : false;
        }
        
        public static string JsonToToken(string json)
        {
            var result = XsollaClientHelpers.FromJson<XsollaStoreClientAccessTokenResult>(json);
            return result?.token;
        }
        
        public static string JsonToStorefront(string json)
        {
            var result = XsollaClientHelpers.FromJson<XsollaStoreClientStorefrontResult>(json);
            return result?.storefront;
        }
        
        public static bool JsonToDistribution(string json)
        {
            var result = XsollaClientHelpers.FromJson<XsollaStoreClientDistributionResult>(json);
            return result?.isRunningInAlternativeDistribution ?? false;
        }

        public static string EmptyJson => "{}";
        
        public static string PurchaseToJson(
            string sku, string developerPayload, string externalId,
            [CanBeNull] string paymentToken = null, int? paymentMethodId = null, bool allowTokenOnlyFinishedStatusWithoutOrderId = false,
            [CanBeNull] string externalTransactionToken = null
        )
        {
            var data = new XsollaStoreClientPaymentData(
                sku, developerPayload, externalId, paymentToken, paymentMethodId, allowTokenOnlyFinishedStatusWithoutOrderId,
                externalTransactionToken
            );
            return XsollaClientHelpers.ToJson(data);
        }
        
        public static string ConsumeToJson(string sku, int quantity, string transactionId, string receipt)
        {
            var data = new XsollaStoreClientConsumeData(sku, quantity, transactionId, receipt);
            return XsollaClientHelpers.ToJson(data);
        }
        
        public static string ReceiptToJson(XsollaStoreClientPurchaseReceipt purchaseReceipt) => XsollaClientHelpers.ToJson(purchaseReceipt);
        public static XsollaStoreClientPurchaseReceipt JsonToReceipt(string json) => XsollaClientHelpers.FromJson<XsollaStoreClientPurchaseReceipt>(json);
        public static bool TryJsonToReceipt(string json, out XsollaStoreClientPurchaseReceipt receipt)
        {
            var result = JsonToReceipt(json);
            receipt = result;
            return result != null;
        }

        public static XsollaStoreClientError ParsePurchaseError(string error)
        {
            if (string.IsNullOrEmpty(error))
                return new XsollaStoreClientError("Unknown error!", XsollaStoreClientPurchaseErrorCode.Unknown);

            // Try JSON shape: { message: "...", code: "Cancelled" | 4 }
            try
            {
                var token = JToken.Parse(string.IsNullOrWhiteSpace(error) ? "{}" : error);
                if (token != null && token.Type == JTokenType.Object)
                {
                    var obj = (JObject)token;
                    var messageToken = obj["message"];
                    var codeToken = obj["code"];
                    string message = messageToken?.Type == JTokenType.String ? (string)messageToken : null;
                    var code = XsollaStoreClientPurchaseErrorCode.Unknown;
                    if (codeToken != null)
                    {
                        if (codeToken.Type == JTokenType.String)
                        {
                            var codeStr = ((string)codeToken) ?? string.Empty;
                            
                            if (string.Equals(codeStr, "UserCancelled"))
                                codeStr = "Cancelled"; // backward compatibility
                            
                            if (!string.IsNullOrEmpty(codeStr) && System.Enum.TryParse<XsollaStoreClientPurchaseErrorCode>(codeStr, true, out var parsedStr))
                                code = parsedStr;
                        }
                        else if (codeToken.Type == JTokenType.Integer)
                        {
                            var codeInt = (int)codeToken;
                            if (System.Enum.IsDefined(typeof(XsollaStoreClientPurchaseErrorCode), codeInt))
                                code = (XsollaStoreClientPurchaseErrorCode)codeInt;
                        }
                    }

                    if (!string.IsNullOrEmpty(message))
                        return new XsollaStoreClientError(message, code);
                }
            }
            catch { /* not a JSON, fall back to message */ }
            
            return new XsollaStoreClientError(error, XsollaStoreClientPurchaseErrorCode.Unknown);
        }
    }
}
