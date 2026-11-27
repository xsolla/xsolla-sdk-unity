namespace Xsolla.SDK.Store
{
    /// <summary>
    /// How a product is used up, in Unity IAP-neutral terms; it decides whether finishing a purchase consumes it.
    /// <para/>
    /// Every value is named after its namesake in Unity IAP's <c>ProductType</c>, so the logs print the same type
    /// names. Bindings map with a <c>switch</c>, never by casting: the numeric values here are not tied to Unity's.
    /// </summary>
    internal enum XsollaPurchasingProductType
    {
        /// <summary>Can be purchased again once consumed; the only type a finished purchase is consumed for.</summary>
        Consumable = 0,

        /// <summary>Owned indefinitely, never consumed; also the fallback for any type no other value describes.</summary>
        NonConsumable = 1,

        /// <summary>Valid for a finite window, never consumed.</summary>
        Subscription = 2
    }
}
