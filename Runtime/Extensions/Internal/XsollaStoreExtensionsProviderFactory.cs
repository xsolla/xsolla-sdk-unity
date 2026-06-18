using Xsolla.SDK.Common.Extensions;
using Xsolla.SDK.Store;

namespace Xsolla.SDK.Extensions
{
    /// <summary>
    /// Factory for registering and unregistering the extensions handler with the Xsolla Store SDK.
    /// </summary>
    internal static class XsollaStoreExtensionsProviderFactory
    {
        /// <summary>
        /// Registers the extensions handler if not already registered.
        /// </summary>
        /// <returns>The registered handler instance.</returns>
        public static IXsollaStoreClientExtensionsHandler Register()
        {
            if (XsollaStoreClientExtensionsProvider.Handler == null)
                XsollaStoreClientExtensionsProvider.Register(new XsollaStoreClientExtensionsProviderHandler());

            return XsollaStoreClientExtensionsProvider.Handler;
        }

        /// <summary>
        /// Unregisters the current extensions handler if one is registered.
        /// </summary>
        public static void Unregister()
        {
            if (XsollaStoreClientExtensionsProvider.Handler != null)
                XsollaStoreClientExtensionsProvider.Unregister(XsollaStoreClientExtensionsProvider.Handler);
        }

        private class XsollaStoreClientExtensionsProviderHandler : IXsollaStoreClientExtensionsHandler
        {
            public RetryPolicies GetRetryPolicies() =>
                XsollaStoreClientExtensionsManager.Instance().Settings.retryPolicies;

            public ProductFetchSettings GetProductFetchSettings() =>
                XsollaStoreClientExtensionsManager.Instance().Settings.productFetchSettings;
        }
    }
}