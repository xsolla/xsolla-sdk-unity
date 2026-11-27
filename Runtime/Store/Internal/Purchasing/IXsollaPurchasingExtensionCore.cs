using System;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// Xsolla store extension members shared by every supported Unity IAP version.
    /// <para/>
    /// Members use no Unity IAP types; the public extension interface of each Unity IAP version declares its own
    /// members, including the ones it shares with this contract.
    /// </summary>
    internal interface IXsollaPurchasingExtensionCore
    {
        /// <summary>
        /// Restores previous transactions.
        /// </summary>
        /// <param name="onSuccess">Callback invoked with true if restoration succeeded.</param>
        /// <param name="onError">Callback invoked with error message if restoration failed.</param>
        void RestoreTransactions(Action<bool> onSuccess, Action<string> onError);

        /// <summary>
        /// Gets the access token for the store.
        /// </summary>
        /// <param name="onSuccess">Callback invoked with access token on success.</param>
        /// <param name="onError">Callback invoked with error message on failure.</param>
        void GetAccessToken(Action<string> onSuccess, Action<string> onError);

        /// <summary>
        /// Updates the access token for the store.
        /// </summary>
        /// <param name="token">The new access token.</param>
        /// <param name="onSuccess">Callback invoked on successful token update.</param>
        /// <param name="onError">Callback invoked with error message on failure.</param>
        void UpdateAccessToken(string token, Action onSuccess, Action<string> onError);

        /// <summary>
        /// Gets the Apple Storefront identifier.
        /// </summary>
        /// <param name="onSuccess">Callback invoked with storefront identifier on success.</param>
        /// <param name="onError">Callback invoked with error message on failure.</param>
        void GetAppleStorefront(Action<string> onSuccess, Action<string> onError);

        /// <summary>
        /// Initiates a purchase for a product by ID.
        /// </summary>
        /// <param name="productId">Product ID (SKU).</param>
        /// <param name="args">Purchase arguments.</param>
        void InitiatePurchase(string productId, XsollaStoreClientPurchaseArgs args);
    }
}
