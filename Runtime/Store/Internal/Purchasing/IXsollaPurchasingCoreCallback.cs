using System.Collections.Generic;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// What the purchasing core reports back to the Unity IAP binding that drives it, unprompted or once per request.
    /// <para/>
    /// The binding translates each call into its Unity IAP line's callback, or ignores one its line does not use.
    /// A binding that takes purchases existing at startup with the first catalog fetch
    /// (<see cref="XsollaPurchasingCoreOptions.restoresWithFirstFetch"/>) gets them as
    /// <see cref="XsollaPurchasingProduct.restoredPurchase"/> and need not use the purchases fetch. The 4.x binding
    /// ignores the purchases fetch, as its own <c>OnAllPurchasesRetrieved</c> would clear the receipt of every product
    /// missing from the list, and ignores the initialization result.
    /// Consume results are not reported here: each consume request carries its own callbacks, so a binding can match
    /// a result to the order it confirms.
    /// The core calls every member on the thread its store client calls back on, the Unity main thread in a player.
    /// </summary>
    internal interface IXsollaPurchasingCoreCallback
    {
        /// <summary>The store client reported a successful initialization.</summary>
        void OnInitialized();

        /// <summary>The configuration failed to resolve, or the store client reported a failed initialization.</summary>
        /// <param name="message">Description of the failure.</param>
        void OnInitializeFailed(string message);

        /// <summary>The requested catalog was fetched.</summary>
        /// <param name="products">The products the store knows; requested SKUs it does not know are absent.</param>
        void OnProductsFetched(IReadOnlyList<XsollaPurchasingProduct> products);

        /// <summary>The catalog could not be fetched.</summary>
        /// <param name="message">Description of the failure.</param>
        void OnProductsFetchFailed(string message);

        /// <summary>
        /// The user's unconsumed purchases were fetched.
        /// <para/>
        /// Lists every purchase, delivered before or not. Those not delivered before are also reported through
        /// <see cref="OnPurchaseSucceeded"/> once the first catalog fetch completes, unless
        /// <see cref="XsollaPurchasingCoreOptions.dropsPurchasesBeforeFirstFetch"/> drops them; their order relative
        /// to this call is not guaranteed.
        /// </summary>
        /// <param name="purchases">Every unconsumed purchase.</param>
        void OnPurchasesFetched(IReadOnlyList<XsollaPurchasingPurchase> purchases);

        /// <summary>The user's purchases could not be fetched.</summary>
        /// <param name="message">Description of the failure.</param>
        void OnPurchasesFetchFailed(string message);

        /// <summary>
        /// A purchase is ready for delivery: a fresh purchase, a native re-delivery or a restored one.
        /// <para/>
        /// A purchase with a transaction id is handed over at most once per transaction and SKU in a session,
        /// counting a hand-over as <see cref="XsollaPurchasingProduct.restoredPurchase"/>. Delivery waits for the first
        /// catalog fetch after initialization.
        /// </summary>
        /// <param name="purchase">The purchase to deliver.</param>
        void OnPurchaseSucceeded(XsollaPurchasingPurchase purchase);

        /// <summary>A purchase failed.</summary>
        /// <param name="failure">The failed purchase.</param>
        void OnPurchaseFailed(XsollaPurchasingFailure failure);
    }
}
