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
            /// When enabled, if the app is killed during a payment flow in an external browser,
            /// tapping "Back to Game" in PayStation relaunches the app from its main launcher
            /// activity instead of silently failing.
            /// <para/>
            /// Requires <see cref="WebViewType.External"/>.
            /// <para/>
            /// Off by default.
            /// </summary>
            public bool redirectAppRelaunchEnabled;

            /// <summary>
            /// Controls how the SDK reports restored purchases when the Inventory API returns a
            /// combined quantity for a SKU rather than individual transaction records — a known limitation
            /// of the Inventory API (e.g. three separate purchases of SKU X are surfaced as quantity 3
            /// with no way to recover the original transaction boundaries).
            /// <para/>
            /// When <c>false</c> (default), the SDK expands the combined quantity back into
            /// individual quantity-1 purchase notifications.
            /// A hard cap applies: if the combined quantity exceeds the internal threshold,
            /// the SKU is collapsed regardless of this flag and a warning is logged.
            /// Prefer this for discrete consumables with low expected quantities
            /// (e.g. a few health potions, a handful of level-skip items).
            /// <para/>
            /// When <c>true</c>, the SDK reports the full combined quantity as a single purchase
            /// notification per SKU, bypassing the expansion entirely.
            /// Prefer this for high-quantity SKUs where per-unit expansion is impractical
            /// (e.g. soft currency, stackable resources that players accumulate in the thousands).
            /// <para/>
            /// This flag has no effect when the Events API is used for purchase tracking, as the
            /// Events API exposes individual transaction records directly and does not require
            /// any quantity expansion or collapsing workarounds.
            /// </summary>
            public bool collapseRestoredMultiUnitPurchases;

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

            /// <summary>
            /// Enables app relaunch from PayStation redirect on cold start.
            /// When the app is killed during a payment flow in an external browser,
            /// tapping "Back to Game" in PayStation relaunches the app instead of silently failing.
            /// <para/>
            /// Requires <see cref="WebViewType.External"/>.
            /// <para/>
            /// Off by default.
            /// </summary>
            /// <param name="redirectAppRelaunchEnabled">True to enable app relaunch on cold-start redirect.</param>
            public AdvancedSettingsAndroid SetRedirectAppRelaunchEnabled(bool redirectAppRelaunchEnabled)
            {
                this.redirectAppRelaunchEnabled = redirectAppRelaunchEnabled;
                return this;
            }

            /// <summary>
            /// Sets whether restored multi-unit purchases are collapsed into a single per-SKU
            /// notification or expanded into individual quantity-1 notifications.
            /// <para/>
            /// See <see cref="collapseRestoredMultiUnitPurchases"/> for full details and caveats.
            /// </summary>
            /// <param name="collapseRestoredMultiUnitPurchases">
            /// <c>true</c> to report the combined quantity as one notification per SKU;
            /// <c>false</c> (default) to expand into individual quantity-1 notifications.
            /// </param>
            public AdvancedSettingsAndroid SetCollapseRestoredMultiUnitPurchases(bool collapseRestoredMultiUnitPurchases)
            {
                this.collapseRestoredMultiUnitPurchases = collapseRestoredMultiUnitPurchases;
                return this;
            }
        }
    }
}
