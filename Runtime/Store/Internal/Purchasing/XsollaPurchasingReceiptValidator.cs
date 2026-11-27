using System;
using JetBrains.Annotations;
using Xsolla.SDK.Common;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// The Unity IAP-neutral receipt validation behind the validator type of each Unity IAP binding: it sends the
    /// purchase payload to the Xsolla Store backend through the store client.
    /// <para/>
    /// Takes either the receipt string Unity IAP hands out, which wraps the payload in its <c>Payload</c> field, or the
    /// bare payload, which is sent as is.
    /// </summary>
    internal sealed class XsollaPurchasingReceiptValidator
    {
        /// <summary>
        /// Log tag; the Unity IAP 4.x validator logged under it before this logic moved here, so existing log filters
        /// keep matching.
        /// </summary>
        private const string Tag = "XsollaPurchasingStoreValidator";

        /// <summary>
        /// Reference to the store client responsible for backend validation.
        /// </summary>
        private readonly IXsollaStoreClient _storeClient;

        /// <summary>
        /// Creates a validator that validates through the given store client.
        /// </summary>
        /// <param name="storeClient">The store client to use for validation.</param>
        public XsollaPurchasingReceiptValidator(IXsollaStoreClient storeClient)
        {
            _storeClient = storeClient;
        }

        /// <summary>
        /// Validates the given purchase receipt by sending it to the Xsolla Store backend.
        /// </summary>
        /// <param name="receipt">The receipt string from Unity IAP, or the bare payload.</param>
        /// <param name="completionHandler">
        /// Callback function invoked after validation completes.
        /// The first parameter indicates success (true/false),
        /// and the second provides an error message if validation fails.
        /// </param>
        public void Validate(string receipt, Action<bool, string> completionHandler)
        {
            XsollaLogger.Debug(Tag, "Validate");

            // Attempt to extract a payload from the receipt string.
            var payloadStr = ExtractPayloadAsString(receipt);

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

        /// <summary>
        /// Extracts the unescaped payload from a Unity IAP receipt string.
        /// </summary>
        /// <param name="receipt">The receipt string from Unity IAP.</param>
        /// <returns>The payload, or null if the receipt is empty, has no payload or can't be read.</returns>
        [CanBeNull]
        public static string ExtractPayloadAsString(string receipt)
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
    }
}
