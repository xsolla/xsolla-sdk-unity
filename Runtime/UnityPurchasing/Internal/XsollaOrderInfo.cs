#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Xsolla.SDK.UnityPurchasing
{
    internal sealed class XsollaOrderInfo : IOrderInfo
    {
        internal XsollaOrderInfo(string rawReceipt, string transactionId)
        {
            TransactionID = transactionId ?? string.Empty;
            Receipt = JsonUtility.ToJson(new UnifiedReceipt
            {
                Store = XsollaPurchasingStore.Name,
                TransactionID = TransactionID,
                Payload = rawReceipt ?? string.Empty
            });
            PurchasedProductInfo = new List<IPurchasedProductInfo>();
        }

        public IAppleOrderInfo Apple => null;
        public IGoogleOrderInfo Google => null;
        public List<IPurchasedProductInfo> PurchasedProductInfo { get; set; }
        public string TransactionID { get; }
        public string Receipt { get; }
    }
}
#endif
