#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using JetBrains.Annotations;
using System;
using UnityEngine;
using UnityEngine.Purchasing;
using Xsolla.SDK.Common;
using Xsolla.SDK.Store;

namespace Xsolla.SDK.UnityPurchasing
{
    /// <summary>
    /// <see cref="UnityEngine.Purchasing.Product"/>'s <see cref="UnityEngine.Purchasing.Product.receipt"/> in a deserialized form.
    /// </summary>
    internal readonly struct PurchaseEventPayload
    {
        public enum OrderStatus
        {
            Unknown = 0,
            New = 1,
            Paid = 2,
            Done = 3,
            Canceled = 4,
            Restored = 5,
            RestoredByEvent = 6,
            Free = 7
        }

        public readonly string productId;
        public readonly long orderId;
        public readonly string invoiceId;
        public readonly string transactionId;
        public readonly string receipt;
        public readonly OrderStatus orderStatus;
        public readonly XsollaStoreClientPurchasedProduct.Origin origin;

        internal PurchaseEventPayload(
            [NotNull] PurchaseEventArgsExtensions.DeserializedPayload deserializedPayload
        )
        {
            productId = deserializedPayload.productId;
            orderId = deserializedPayload.orderId;
            invoiceId = deserializedPayload.invoiceId;
            transactionId = deserializedPayload.transactionId;
            receipt = deserializedPayload.receipt;

            this.orderStatus = Enum.TryParse<OrderStatus>(deserializedPayload.orderStatus, ignoreCase: true, out var orderStatus)
                ? orderStatus
                : OrderStatus.Unknown;

            this.origin = Enum.TryParse<XsollaStoreClientPurchasedProduct.Origin>(deserializedPayload.origin, ignoreCase: true, out var origin)
                && Enum.IsDefined(typeof(XsollaStoreClientPurchasedProduct.Origin), origin)
                ? origin
                : XsollaStoreClientPurchasedProduct.Origin.Unknown;
        }

        public override string ToString() =>
            $"Payload [productId='{productId}', orderId={orderId}, invoiceId='{invoiceId}', " +
            $"transactionId='{transactionId}', receipt='{receipt}', orderStatus={orderStatus}, origin={origin}]";
    }

    internal static class PurchaseEventArgsExtensions
    {
        [Serializable]
        internal sealed class DeserializedPayload
        {
            public string productId;
            public long orderId;
            public string invoiceId;
            public string transactionId;
            public string receipt;
            public string orderStatus;
            public string origin;

            public DeserializedPayload() {}

            public DeserializedPayload(
                string productId, long orderId, string invoiceId, string transactionId,
                string receipt, string orderStatus, string origin
            )
            {
                this.productId = productId;
                this.orderId = orderId;
                this.invoiceId = invoiceId;
                this.transactionId = transactionId;
                this.receipt = receipt;
                this.orderStatus = orderStatus.ToLower();
                this.origin = origin;
            }
        }

        [CanBeNull]
        internal static string ExtractPayloadAsString(string receipt)
        {
            try
            {
                if (string.IsNullOrEmpty(receipt))
                    return null;

                const string PAYLOAD_NEEDLE = "{\"Payload\":\"";
                const string PAYLOAD_TAIL_NEEDLE = "}\",\"Store";

                var payloadKeyIndex = receipt.IndexOf(PAYLOAD_NEEDLE, StringComparison.Ordinal);
                if (payloadKeyIndex < 0)
                    return null;

                var payloadStartIndex = payloadKeyIndex + PAYLOAD_NEEDLE.Length;

                var payloadEndIndex = receipt.LastIndexOf(PAYLOAD_TAIL_NEEDLE, StringComparison.Ordinal);
                if (payloadEndIndex < 0)
                    return null;

                var escapedPayloadStr = receipt.Substring(
                    payloadStartIndex, payloadEndIndex - payloadStartIndex + 1
                );

                var unescapedPayloadStr = UnescapeJsonString(escapedPayloadStr);

                return unescapedPayloadStr;
            }
            catch (Exception e)
            {
                XsollaLogger.Debug("ExtractPayload", e.Message);
                return null;
            }

            static string UnescapeJsonString(string str)
            {
                if (string.IsNullOrEmpty(str))
                    return str;

                var sb = new System.Text.StringBuilder(str.Length);

                for (var i = 0; i < str.Length; i++)
                {
                    var ch = str[i];

                    if (ch == '\\' && i + 1 < str.Length)
                    {
                        var nextCh = str[i + 1];

                        if (nextCh is '"' or '\\')
                        {
                            sb.Append(nextCh);
                            i++;
                            continue;
                        }
                    }

                    sb.Append(ch);
                }

                return sb.ToString();
            }
        }

        internal static PurchaseEventPayload? ExtractPayload(this PurchaseEventArgs purchaseEventArgs)
        {
            if (!purchaseEventArgs.purchasedProduct.hasReceipt) return null;

            var payloadStr = ExtractPayloadAsString(purchaseEventArgs.purchasedProduct.receipt);
            if (!string.IsNullOrEmpty(payloadStr)) return null;

            var payload = JsonUtility.FromJson<DeserializedPayload>(payloadStr);

            return payload != null ? new PurchaseEventPayload(payload) : null;
        }
    }
}
#endif
