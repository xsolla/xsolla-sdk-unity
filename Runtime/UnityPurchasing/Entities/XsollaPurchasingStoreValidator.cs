#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using System;
using Xsolla.SDK.Common;
using Xsolla.SDK.Store;

namespace Xsolla.SDK.UnityPurchasing
{
    /// <summary>
    /// Handles validation of in-app purchase receipts using the Xsolla Store backend.
    /// </summary>
    public class XsollaPurchasingStoreValidator
    {
        private const string Tag = "XsollaPurchasingStoreValidator";
        
        /// <summary>
        /// Reference to the store client responsible for backend validation.
        /// </summary>
        private readonly IXsollaStoreClient _storeClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="XsollaPurchasingStoreValidator"/> class.
        /// </summary>
        /// <param name="store">The store client to use for validation.</param>
        internal XsollaPurchasingStoreValidator(IXsollaStoreClient store)
        {
            _storeClient = store;
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
        public void Validate(string receipt, Action<bool, string> completionHandler)
        {
            XsollaLogger.Debug(Tag, "Validate");
            
            // Attempt to extract a payload from the receipt string.
            var payloadStr = PendingOrderExtensions.ExtractPayloadAsString(receipt);

            // Fallback to raw receipt if extraction fails or returns empty.
            if (string.IsNullOrEmpty(payloadStr))
            {
                payloadStr = receipt;
            }

            // Send the payload to the store client for backend validation.
            _storeClient.ValidatePurchase(
                payloadStr,
                onSuccess: result => completionHandler?.Invoke(result, null),
                onError: error => completionHandler?.Invoke(false, error)
            );
        }
    }
}
#endif
