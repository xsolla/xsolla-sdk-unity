namespace Xsolla.SDK.Store
{
    /// <summary>
    /// A failed purchase of one product, as the purchasing core reports it to a Unity IAP binding.
    /// <para/>
    /// Carries a SKU because both Unity IAP lines need a product to report a failure against; the core drops a
    /// failure it cannot attribute to one.
    /// </summary>
    internal sealed class XsollaPurchasingFailure
    {
        /// <summary>SKU of the product whose purchase failed.</summary>
        public readonly string sku;

        /// <summary>Why the purchase failed.</summary>
        public readonly XsollaPurchasingFailureReason reason;

        /// <summary>Human-readable description of the failure, passed through to Unity IAP.</summary>
        public readonly string message;

        /// <summary>
        /// Initializes a new instance of the <see cref="XsollaPurchasingFailure"/> class.
        /// </summary>
        /// <param name="sku">SKU of the product whose purchase failed.</param>
        /// <param name="reason">Why the purchase failed.</param>
        /// <param name="message">Human-readable description of the failure.</param>
        public XsollaPurchasingFailure(string sku, XsollaPurchasingFailureReason reason, string message)
        {
            this.sku = sku;
            this.reason = reason;
            this.message = message;
        }

        public override string ToString() => $"Failure [sku='{sku}', reason={reason}, message='{message}']";
    }
}
