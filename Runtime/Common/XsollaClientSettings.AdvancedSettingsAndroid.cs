using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Xsolla.SDK.Common
{
    public partial class XsollaClientSettings
    {
        /// <summary>
        /// Various advanced Android-oriented settings.
        /// </summary>
        [Serializable]
        public class AdvancedSettingsAndroid
        {
            [CanBeNull] public List<string> userBlacklistedProviders;

            public bool internalProviderBlacklistEnabled;

            [CanBeNull] public List<string> userBlacklistedTWAProviders;

            public bool internalTWAProviderBlacklistEnabled;

            [CanBeNull] public Dictionary<string, float> userProviderPriorityWeights;

            public bool fallbackToFirstAvailableTWAProvider;

            /// <summary>
            /// When enabled, the purchase error callback distinguishes between a user closing
            /// the payment screen without paying and a payment that was attempted but failed
            /// (reported as an error, not a cancellation).
            /// When disabled, both cases are reported as cancellations.
            /// <para/>
            /// Off by default.
            /// </summary>
            public bool queryCancellationReasonEnabled;

            /// <summary>
            /// Sets a set of providers that need to be excluded from being used by the
            /// SDK when opening a custom tab or a trusted web activity.
            /// <para/>
            /// Combined with the internal provider blacklist.
            /// <para/>
            /// Requires <see cref="WebViewType.System"/> or <see cref="WebViewType.Trusted"/>.
            /// </summary>
            /// <param name="userBlacklistedProviders">A set of blacklisted providers.</param>
            public AdvancedSettingsAndroid SetUserBlacklistedProviders([CanBeNull] HashSet<string> userBlacklistedProviders)
            {
                this.userBlacklistedProviders = userBlacklistedProviders?.ToList();
                return this;
            }

            /// <summary>
            /// Sets whether the SDK's internal provider blacklist is enabled.
            /// <para/>
            /// Requires <see cref="WebViewType.System"/> or <see cref="WebViewType.Trusted"/>.
            /// </summary>
            /// <param name="internalProviderBlacklistEnabled">Is internal provider blacklist enabled?</param>
            public AdvancedSettingsAndroid SetInternalProviderBlacklistEnabled(bool internalProviderBlacklistEnabled)
            {
                this.internalProviderBlacklistEnabled = internalProviderBlacklistEnabled;
                return this;
            }

            /// <summary>
            /// Sets a set of providers that need to be excluded from being used by the
            /// SDK when opening a trusted web activity.
            /// <para/>
            /// Combined with the internal TWA provider blacklist.
            /// <para/>
            /// Has a lower priority over <see cref="SetUserBlacklistedProviders"/>.
            /// <para/>
            /// Requires <see cref="WebViewType.System"/> or <see cref="WebViewType.Trusted"/>.
            /// </summary>
            /// <param name="userBlacklistedTWAProviders">A set of blacklisted providers.</param>
            public AdvancedSettingsAndroid SetUserBlacklistedTWAProviders([CanBeNull] HashSet<string> userBlacklistedTWAProviders)
            {
                this.userBlacklistedTWAProviders = userBlacklistedTWAProviders?.ToList();
                return this;
            }

            /// <summary>
            /// Sets whether the SDK's internal TWA provider blacklist is enabled.
            /// <para/>
            /// Requires <see cref="WebViewType.System"/> or <see cref="WebViewType.Trusted"/>.
            /// </summary>
            /// <param name="internalTWAProviderBlacklistEnabled">Is internal provider blacklist enabled?</param>
            public AdvancedSettingsAndroid SetInternalTWAProviderBlacklistEnabled(bool internalTWAProviderBlacklistEnabled)
            {
                this.internalTWAProviderBlacklistEnabled = internalTWAProviderBlacklistEnabled;
                return this;
            }

            /// <summary>
            /// Sets a collection of provider name/weight values that influence how a provider
            /// is picked up upon opening a custom tab or a trusted web activity.
            /// <para/>
            /// Requires <see cref="WebViewType.System"/> or <see cref="WebViewType.Trusted"/>.
            /// </summary>
            /// <param name="userProviderPriorityWeights">A collection of provider name, weight values.</param>
            public AdvancedSettingsAndroid SetUserProviderPriorityWeights([CanBeNull] Dictionary<string, float> userProviderPriorityWeights)
            {
                this.userProviderPriorityWeights = userProviderPriorityWeights;
                return this;
            }

            /// <summary>
            /// Sets whether the SDK should fall back to the first available TWA-compatible provider
            /// if the currently prioritized provider does not support TWA and TWA was explicitly requested.
            /// If disabled, the SDK will still try to open TWA with the unsupported browser, which will
            /// most likely result in a regular custom tab being opened instead.
            /// <para/>
            /// Requires <see cref="WebViewType.System"/> or <see cref="WebViewType.Trusted"/>.
            /// </summary>
            /// <param name="fallbackToFirstAvailableTWAProvider">Should the fallback be enabled?</param>
            public AdvancedSettingsAndroid SetFallbackToFirstAvailableTWAProvider(bool fallbackToFirstAvailableTWAProvider)
            {
                this.fallbackToFirstAvailableTWAProvider = fallbackToFirstAvailableTWAProvider;
                return this;
            }
            /// <summary>
            /// Enables distinguishing between a user closing the payment screen without paying
            /// and a payment that was attempted but failed (reported as an error, not a cancellation).
            /// When disabled, both cases are reported as cancellations.
            /// <para/>
            /// Off by default.
            /// </summary>
            /// <param name="queryCancellationReasonEnabled">True to distinguish failed payments from user cancellations.</param>
            public AdvancedSettingsAndroid SetQueryCancellationReasonEnabled(bool queryCancellationReasonEnabled)
            {
                this.queryCancellationReasonEnabled = queryCancellationReasonEnabled;
                return this;
            }
        }
    }
}
