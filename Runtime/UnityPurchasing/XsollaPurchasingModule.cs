#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using UnityEngine.Purchasing;
using Xsolla.SDK.Common;

namespace Xsolla.SDK.UnityPurchasing
{
    /// <summary>
    /// Registers Xsolla as a Unity IAP 5 custom store.
    /// </summary>
    public sealed class XsollaPurchasingModule
    {
        public static string StoreName => XsollaPurchasingStore.Name;

        public sealed class Builder
        {
            private XsollaClientConfiguration _configuration = XsollaClientConfiguration.Builder.Empty();

            public static Builder Create() => new Builder();

            public Builder SetConfiguration(XsollaClientConfiguration configuration)
            {
                _configuration = configuration;
                return this;
            }

            public XsollaPurchasingModule Build() => new XsollaPurchasingModule(_configuration);
        }

        private readonly XsollaClientConfiguration _configuration;
        private bool _configured;

        private XsollaPurchasingModule(XsollaClientConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Registers Xsolla and selects it as the default Unity IAP store.
        /// Call this before requesting Unity IAP services.
        /// </summary>
        public void Configure()
        {
            if (_configured)
                return;

            XsollaLogger.SetLogLevel(_configuration.logLevel);
            var store = new XsollaPurchasingStore(_configuration);

            UnityIAPServices.AddNewCustomStore(new XsollaPurchasingStoreWrapper(store));
            UnityIAPServices.AddNewExtendedPurchaseService(
                StoreName,
                baseService => new XsollaPurchasingService(baseService, store));
            UnityIAPServices.SetStoreAsDefault(StoreName);

            _configured = true;
        }

        /// <summary>
        /// Configures Xsolla and returns a Unity IAP 5 controller for it.
        /// </summary>
        public StoreController CreateStoreController()
        {
            Configure();
            return UnityIAPServices.StoreController(StoreName);
        }

        /// <summary>
        /// Gets Xsolla-specific purchasing operations from the Unity IAP 5 purchase service.
        /// </summary>
        public IXsollaPurchasingStoreExtension GetStoreExtension()
        {
            Configure();
            return (IXsollaPurchasingStoreExtension)UnityIAPServices.Purchase(StoreName);
        }
    }
}
#endif
