#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using System;
using Xsolla.SDK.Store;

namespace Xsolla.SDK.UnityPurchasing
{
    /// <summary>
    /// Handles validation of in-app purchase receipts using the Xsolla Store backend.
    /// </summary>
    public class XsollaPurchasingStoreValidator
    {
        /// <summary>
        /// The Unity IAP-neutral validator that does the work.
        /// </summary>
        private readonly XsollaPurchasingReceiptValidator _validator;

        /// <summary>
        /// Initializes a new instance of the <see cref="XsollaPurchasingStoreValidator"/> class.
        /// </summary>
        /// <param name="store">The store client to use for validation.</param>
        internal XsollaPurchasingStoreValidator(IXsollaStoreClient store)
        {
            _validator = new XsollaPurchasingReceiptValidator(store);
        }

        /// <summary>
        /// Validates the given purchase receipt by sending it to the Xsolla Store backend.
        /// </summary>
        /// <param name="receipt">The receipt string from Unity IAP.</param>
        /// <param name="completionHandler">
        /// Callback function invoked after validation completes.
        /// The first parameter indicates success (true/false),
        /// and the second provides an error message if validation fails.
        /// </param>
        public void Validate(string receipt, Action<bool, string> completionHandler) =>
            _validator.Validate(receipt, completionHandler);
    }
}
#endif
