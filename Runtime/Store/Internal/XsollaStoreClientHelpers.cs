using JetBrains.Annotations;
using Xsolla.SDK.Common;
using Xsolla.SDK.Utils;
using Newtonsoft.Json;
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

        /// <summary>
        /// Parses an error string from a platform implementation. Named for purchases, where it
        /// started, but the shape is the same for every native error — a catalog fetch that failed
        /// sends it too — and anything that isn't this JSON is kept as the message verbatim. So is an error JSON
        /// without a message, though its code and the rest are still read.
        /// </summary>
        public static XsollaStoreClientError ParsePurchaseError(string error)
        {
            if (string.IsNullOrEmpty(error))
                return new XsollaStoreClientError("Unknown error!", XsollaStoreClientPurchaseErrorCode.Unknown);

            // Try JSON shape: { message: "...", code: "Cancelled" | 4, statusCode: 422, errorCode: 1006, reason: "ProductUnavailable" }
            // statusCode/errorCode are there only when an Xsolla API response is what failed; reason only when the
            // platform client could classify the failure.
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

                    return new XsollaStoreClientError(string.IsNullOrEmpty(message) ? error : message, code)
                    {
                        statusCode = ReadInt(obj["statusCode"]),
                        errorCode = ReadInt(obj["errorCode"]),
                        errorMessage = ReadString(obj["errorMessage"]),
                        reason = ReadReason(obj["reason"]),
                    };
                }
            }
            catch { /* not a JSON, fall back to message */ }
            
            return new XsollaStoreClientError(error, XsollaStoreClientPurchaseErrorCode.Unknown);
        }

        /// <summary>Reads an optional integer field, as zero when it is absent or isn't a number.</summary>
        private static int ReadInt(JToken token) => token?.Type == JTokenType.Integer ? (int)token : 0;

        /// <summary>Reads an optional string field, as null when it is absent or isn't a string.</summary>
        private static string ReadString(JToken token) => token?.Type == JTokenType.String ? (string)token : null;

        /// <summary>
        /// Reads the optional failure reason, as null when it is absent, isn't a string or isn't exactly a value's name:
        /// a number, even in a string, is never read as one, since the values are not part of the error JSON.
        /// <see cref="XsollaPurchasingFailureReason.UserCancelled"/> is read as null too: only the cancel code makes a
        /// cancel, so a tag alone never does.
        /// </summary>
        private static XsollaPurchasingFailureReason? ReadReason(JToken token)
        {
            var name = ReadString(token);
            return name != null
                && name != nameof(XsollaPurchasingFailureReason.UserCancelled)
                && System.Enum.IsDefined(typeof(XsollaPurchasingFailureReason), name)
                ? (XsollaPurchasingFailureReason?) System.Enum.Parse(typeof(XsollaPurchasingFailureReason), name)
                : null;
        }

#nullable disable
        /// <summary>
        /// Writes an optional failure reason as its name and reads one as <see cref="ParsePurchaseError"/> does, so
        /// reading a reason the SDK doesn't know, or a value that is no name at all, gives null instead of throwing.
        /// <para/>
        /// Preserved because Newtonsoft creates it by reflection, from the attribute alone, which managed code
        /// stripping cannot see.
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        internal sealed class FailureReasonConverter : JsonConverter
        {
            public override bool CanConvert(System.Type objectType) => objectType == typeof(XsollaPurchasingFailureReason?);

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) =>
                writer.WriteValue(value?.ToString());

            public override object ReadJson(JsonReader reader, System.Type objectType, object existingValue, JsonSerializer serializer) =>
                ReadReason(JToken.Load(reader));
        }
#nullable restore
    }
}
