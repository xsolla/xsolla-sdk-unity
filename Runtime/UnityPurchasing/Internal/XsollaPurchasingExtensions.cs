#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using System;
using UnityEngine;
using UnityEngine.Purchasing;
using Xsolla.SDK.Common;

namespace Xsolla.SDK.UnityPurchasing
{
    /// <summary>
    /// The Xsolla payload contained in a Unity IAP 5 order receipt.
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

        internal PurchaseEventPayload(PendingOrderExtensions.DeserializedPayload payload)
        {
            productId = payload.productId;
            orderId = payload.orderId;
            invoiceId = payload.invoiceId;
            transactionId = payload.transactionId;
            receipt = payload.receipt;
            orderStatus = Enum.TryParse(payload.orderStatus, true, out OrderStatus status)
                ? status
                : OrderStatus.Unknown;
        }
    }

    internal static class PendingOrderExtensions
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

            public DeserializedPayload()
            {
            }

            public DeserializedPayload(
                string productId,
                long orderId,
                string invoiceId,
                string transactionId,
                string receipt,
                string orderStatus)
            {
                this.productId = productId;
                this.orderId = orderId;
                this.invoiceId = invoiceId;
                this.transactionId = transactionId;
                this.receipt = receipt;
                this.orderStatus = orderStatus;
            }
        }

        internal static string ExtractPayloadAsString(string receipt)
        {
            if (string.IsNullOrEmpty(receipt))
                return null;

            try
            {
                var unifiedReceipt = JsonUtility.FromJson<UnifiedReceipt>(receipt);
                return string.IsNullOrEmpty(unifiedReceipt?.Payload) ? null : unifiedReceipt.Payload;
            }
            catch (Exception exception)
            {
                XsollaLogger.Debug("ExtractPayload", exception.Message);
                return null;
            }
        }

        internal static PurchaseEventPayload? ExtractPayload(this PendingOrder pendingOrder)
        {
            var payloadString = ExtractPayloadAsString(pendingOrder?.Info?.Receipt);
            if (string.IsNullOrEmpty(payloadString))
                return null;

            var payload = JsonUtility.FromJson<DeserializedPayload>(payloadString);
            return payload != null ? new PurchaseEventPayload(payload) : (PurchaseEventPayload?)null;
        }
    }
}
#endif
