#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using JetBrains.Annotations;
using UnityEngine.Purchasing.Extension;
using Xsolla.SDK.Common;

namespace Xsolla.SDK.UnityPurchasing
{
    /// <summary>
    /// Provides a Unity Purchasing module for Xsolla integration.
    /// </summary>
    public class XsollaPurchasingModule : AbstractPurchasingModule
    {
        /// <summary>
        /// Gets the store name for Xsolla Purchasing.
        /// </summary>
        public static string StoreName => XsollaPurchasingStore.Name;

        /// <summary>
        /// Builder for <see cref="XsollaPurchasingModule"/>.
        /// </summary>
        public class Builder
        {
            private XsollaClientConfiguration _configuration = XsollaClientConfiguration.Builder.Empty();

            /// <summary>
            /// Creates a new builder instance.
            /// </summary>
            public static Builder Create() => new Builder();

            /// <summary>
            /// Sets the client configuration.
            /// </summary>
            /// <param name="configuration">Client configuration.</param>
            public Builder SetConfiguration(XsollaClientConfiguration configuration) { _configuration = configuration; return this; }

            /// <summary>
            /// Builds the <see cref="XsollaPurchasingModule"/> instance.
            /// </summary>
            public XsollaPurchasingModule Build() => new XsollaPurchasingModule(_configuration);
        }

        /// <summary>
        /// The client configuration for the purchasing module.
        /// </summary>
        [CanBeNull] private readonly XsollaClientConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of <see cref="XsollaPurchasingModule"/>.
        /// </summary>
        /// <param name="configuration">Client configuration.</param>
        private XsollaPurchasingModule(XsollaClientConfiguration configuration)
        {
            _configuration = configuration;
        }

        #region AbstractPurchasingModule

        /// <summary>
        /// Configures the purchasing module and registers the Xsolla store.
        /// </summary>
        public override void Configure()
        {
            XsollaLogger.Apply(_configuration);

            var store = new XsollaPurchasingStore(_configuration);

            BindExtension<IXsollaPurchasingStoreExtension>(store);
            BindConfiguration<IXsollaPurchasingStoreConfiguration>(store);

            RegisterStore(StoreName, store);
        }

        #endregion
    }
}
#endif