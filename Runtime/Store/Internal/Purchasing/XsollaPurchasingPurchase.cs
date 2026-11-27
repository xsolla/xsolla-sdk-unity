using JetBrains.Annotations;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// A purchase of one product, ready to hand to Unity IAP: a fresh purchase, a native re-delivery or a restored
    /// one.
    /// <para/>
    /// A binding hands Unity IAP what its version takes, such as SKU, receipt and transaction id, or a pending order
    /// with the quantity in its cart item.
    /// </summary>
    internal sealed class XsollaPurchasingPurchase
    {
        /// <summary>SKU of the purchased product.</summary>
        public readonly string sku;

        /// <summary>
        /// Transaction id of the purchase. Null or empty when the platform reports none; such a purchase is neither
        /// deduplicated nor quantity-tracked by the core.
        /// </summary>
        public readonly string transactionId;

        /// <summary>
        /// Serialized <see cref="XsollaStoreClientPurchaseReceipt"/> that Unity IAP exposes as the receipt and
        /// that the purchase validator accepts.
        /// </summary>
        public readonly string receipt;

        /// <summary>Number of units this purchase covers; at least 1.</summary>
        public readonly int quantity;

        /// <summary>
        /// SKU of the purchase request this purchase is the result of, or null when the store client reported it by
        /// itself, outside a purchase request, or it was restored.
        /// <para/>
        /// Differs from <see cref="sku"/> when the store client reports the purchase under the content of its order, such
        /// as a bundle reported under its first content SKU, so a binding can still tell which request it answers. The
        /// Unity IAP 4.x binding ignores it.
        /// </summary>
        [CanBeNull] public readonly string requestedSku;

        /// <summary>
        /// Initializes a new instance of the <see cref="XsollaPurchasingPurchase"/> class.
        /// </summary>
        /// <param name="sku">SKU of the purchased product.</param>
        /// <param name="transactionId">Transaction id of the purchase, or null or empty when the platform reports none.</param>
        /// <param name="receipt">Serialized receipt handed to Unity IAP.</param>
        /// <param name="quantity">Number of units this purchase covers; at least 1.</param>
        /// <param name="requestedSku">SKU of the purchase request this purchase is the result of, or null when there is none.</param>
        public XsollaPurchasingPurchase(
            string sku,
            string transactionId,
            string receipt,
            int quantity,
            [CanBeNull] string requestedSku = null
        )
        {
            this.sku = sku;
            this.transactionId = transactionId;
            this.receipt = receipt;
            this.quantity = quantity;
            this.requestedSku = requestedSku;
        }

        public override string ToString() =>
            $"Purchase [sku='{sku}', transactionId='{transactionId}', quantity={quantity}, requestedSku='{requestedSku}']";
    }
}
