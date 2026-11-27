namespace Xsolla.SDK.Store
{
    /// <summary>
    /// Why a purchase failed, in Unity IAP-neutral terms.
    /// <para/>
    /// Every value has a namesake in the Unity IAP <c>PurchaseFailureReason</c>, so a binding maps each value by name.
    /// Bindings map with a <c>switch</c>, never by casting: the numeric values here are not Unity's. A platform client
    /// names a value in the error JSON's <c>reason</c> key; a binding may still
    /// report a value as another, as the Unity IAP 4.x store reports every reason but a cancellation as a declined
    /// payment.
    /// </summary>
    internal enum XsollaPurchasingFailureReason
    {
        /// <summary>No other value describes the failure; the fallback for any failure no client classified.</summary>
        Unknown = 0,

        /// <summary>The user cancelled the purchase.</summary>
        UserCancelled = 1,

        /// <summary>The store does not sell the product: it was rejected before the payment UI opened.</summary>
        ProductUnavailable = 2,

        /// <summary>The purchase could not start: it failed before the payment UI opened, for a reason other than the product.</summary>
        PurchasingUnavailable = 3
    }
}
