using JetBrains.Annotations;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using UnityEngine.Serialization;
using UnityEngine;

namespace Xsolla.SDK.Common
{
    /// <summary>
    /// Represents the client settings for Xsolla SDK.
    /// </summary>
    [Serializable]
    public partial class XsollaClientSettings
    {
        /// <summary>
        /// Specifies the available theme sizes for the UI.
        /// </summary>
        public enum ThemeSize
        {
            /// <summary>Automatically select the theme size.</summary>
            Auto,
            /// <summary>Small theme size.</summary>
            Small,
            /// <summary>Medium theme size.</summary>
            Medium,
            /// <summary>Large theme size.</summary>
            Large
        }

        /// <summary>
        /// Specifies the available theme styles for the UI.
        /// </summary>
        public enum ThemeStyle
        {
            /// <summary>Automatically select the theme style.</summary>
            Auto,
            /// <summary>Light theme style.</summary>
            Light,
            /// <summary>Dark theme style.</summary>
            Dark,
            /// <summary>Custom theme style.</summary>
            Custom
        }

        /// <summary>
        /// Specifies the close button visibility options.
        /// </summary>
        public enum CloseButton
        {
            /// <summary>Automatically show or hide the close button.</summary>
            Auto,
            /// <summary>Show the close button.</summary>
            Show,
            /// <summary>Hide the close button.</summary>
            Hide
        }

        /// <summary>
        /// Specifies the type of web view to use.
        /// </summary>
        public enum WebViewType
        {
            /// <summary>Default solution for the platform.</summary>
            Auto,
            /// <summary>Embedded in-game web view.</summary>
            InGame,
            /// <summary>System custom tabs solution.</summary>
            System,
            /// <summary>Trusted web activity.</summary>
            Trusted,
            /// <summary>External system browser.</summary>
            External
        }

        /// <summary>
        /// Specifies the orientation lock options for the web view.
        /// </summary>
        public enum OrientationLock
        {
            /// <summary>Automatically select orientation.</summary>
            Auto,
            /// <summary>Portrait orientation.</summary>
            Portrait,
            /// <summary>Landscape orientation.</summary>
            Landscape
        }
        
        /// <summary>
        /// Specifies the available webhooks modes.
        /// </summary>
        public enum WebhooksMode
        {
            /// <summary>Webhooks mode is off.</summary>
            Off,
            /// <summary>Webhooks mode.</summary>
            Webhooks,
            /// <summary>Events API mode.</summary>
            EventsApi
        }
        
        /// <summary>
        /// Specifies visibility options for  logo.
        /// </summary>
        public enum VisibleLogo
        {
            /// <summary>Automatically determine whether to show the logo.</summary>
            Auto,
            /// <summary>Always show the logo.</summary>
            Show,
            /// <summary>Always hide the logo.</summary>
            Hide
        }

        /// <summary>
        /// UI settings for the client.
        /// </summary>
        [Serializable]
        public class UISettings
        {
            /// <summary>Theme size for the UI.</summary>
            [JsonConverter(typeof(StringEnumConverter))]
            public ThemeSize themeSize = ThemeSize.Auto;

            /// <summary>Theme style for the UI.</summary>
            [JsonConverter(typeof(StringEnumConverter))]
            public ThemeStyle themeStyle = ThemeStyle.Auto;

            /// <summary>Custom theme name.</summary>
            public string customTheme = string.Empty;

            /// <summary>Control tint color in HTML RGBA format.</summary>
            public string controlTintColor = ColorUtility.ToHtmlStringRGBA(Color.clear);

            /// <summary>Bar tint color in HTML RGBA format.</summary>
            public string barTintColor = ColorUtility.ToHtmlStringRGBA(Color.clear);
            
            /// <summary>Indicates whether logo is visible.</summary>
            [JsonConverter(typeof(StringEnumConverter))]
            public VisibleLogo visibleLogo = VisibleLogo.Auto;
        }

        /// <summary>
        /// Redirect settings for the client.
        /// </summary>
        [Serializable]
        public class RedirectSettings
        {
            /// <summary>URL to redirect to.</summary>
            public string redirectUrl = string.Empty;

            /// <summary>Text for the redirect button.</summary>
            public string redirectButtonText = string.Empty;

            /// <summary>Delay before redirecting, in seconds.</summary>
            public int redirectDelay = 6;
        }

        /// <summary>Project ID for Xsolla.</summary>
        public int projectId = -1;

        /// <summary>Login ID for authentication.</summary>
        public string loginId = "";

        /// <summary>OAuth client ID.</summary>
        public int oauthClientId = -1;

        /// <summary>URL of the web shop.</summary>
        public string webShopUrl = "";

        /// <summary>Restore local purchases on initialization.</summary>
        [FormerlySerializedAs("restorePurchasesOnInit")]
        public bool localPurchasesRestore = true;
        
        public int localPurchasesRestoreInterval = 0;

        /// <summary>
        /// Controls how multi-unit purchases are reported when restoring from the inventory
        /// (i.e. when the Event API is disabled). The inventory merges all owned units of a SKU
        /// into a single row with a combined quantity, so the individual purchases can't be told
        /// apart. When <c>false</c> (default) each owned unit is reported as its own single-unit
        /// restored purchase with a unique transaction ID, letting every unit be consumed
        /// independently. When <c>true</c> a SKU is reported once with the full quantity, consumed
        /// in a single call.
        /// <para/>
        /// This is the canonical, cross-platform flag and drives both the standalone implementation
        /// and the native Android SDK.
        /// <para/>
        /// Standalone caveat: the restore-on-launch path surfaces purchases to Unity IAP through
        /// <c>OnProductsRetrieved</c>, which carries one purchase per SKU. Split mode therefore drains
        /// only one unit per restore (i.e. one per app launch); set this to <c>true</c> for a
        /// multi-unit SKU to be fully consumed in a single restore on standalone. The native Android
        /// SDK reports per-transaction and honors split directly.
        /// <para/>
        /// Not supported on iOS yet: iOS restores automatically via the native StoreKit observer, which
        /// does not honor this flag, so it has no effect there.
        /// </summary>
        public bool collapseRestoredMultiUnitPurchases = false;

        /// <summary>Type of web view to use.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public WebViewType webViewType = WebViewType.Auto;

        /// <summary>Orientation lock for the web view.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public OrientationLock webViewOrientationLock = OrientationLock.Auto;

        /// <summary>Close button visibility option.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public CloseButton closeButton = CloseButton.Auto;

        /// <summary>File path to the splash screen image for the web view.</summary>
        [CanBeNull]
        public string webViewSplashScreenImageFilepath = string.Empty;

        /// <summary>Drawable ID for the splash screen image on Android.</summary>
        public int webViewSplashScreenImageDrawableIdAndroid = 0;

        /// <summary>UI settings instance.</summary>
        public UISettings uiSettings = new UISettings();

        /// <summary>Redirect settings instance.</summary>
        public RedirectSettings redirectSettings = new RedirectSettings();

        /// <summary>Indicates whether to use the buy button solution.</summary>
        public bool useBuyButtonSolution = false;

        /// <summary>Webhooks mode setting.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public WebhooksMode webhooksMode = WebhooksMode.Off;

        public bool? emailCollectionConsentOptInEnabled;

        public SocialProvider? socialProvider;

        [CanBeNull]
        public AdvancedSettingsAndroid advancedSettingsAndroid;

        /// <summary>
        /// Protected constructor to prevent direct instantiation.
        /// </summary>
        protected XsollaClientSettings() {}

        /// <summary>
        /// Builder class for constructing <see cref="XsollaClientSettings"/> instances.
        /// </summary>
        public class Builder
        {
            /// <summary>Internal settings instance.</summary>
            protected XsollaClientSettings _settings = new XsollaClientSettings();

            /// <summary>
            /// Creates a new builder instance.
            /// </summary>
            public static Builder Create() => new Builder();

            /// <summary>
            /// Updates the builder with an existing settings instance.
            /// </summary>
            /// <param name="settings">Existing settings to update.</param>
            public static Builder Update(XsollaClientSettings settings)
            {
                var builder = new Builder();
                builder._settings = settings;
                return builder;
            }
            
            /// <summary>
            /// Sets the project ID.
            /// </summary>
            /// <param name="projectId">The Xsolla project ID.</param>
            public Builder SetProjectId(int projectId) { _settings.projectId = projectId; return this; }

            /// <summary>
            /// Sets the login ID.
            /// </summary>
            /// <param name="loginId">The login ID for authentication.</param>
            public Builder SetLoginId(string loginId) { _settings.loginId = loginId; return this; }

            /// <summary>
            /// Sets the OAuth client ID (obsolete).
            /// </summary>
            /// <param name="oauthId">The OAuth client ID.</param>
            [Obsolete("Please use SetOAuthClientId instead.")]
            public Builder setOAuthClientId(int oauthId) { SetOAuthClientId(oauthId); return this; }

            /// <summary>
            /// Sets the OAuth client ID.
            /// </summary>
            /// <param name="oauthId">The OAuth client ID.</param>
            public Builder SetOAuthClientId(int oauthId) { _settings.oauthClientId = oauthId; return this; }

            /// <summary>
            /// Sets the web shop URL.
            /// </summary>
            /// <param name="webShopUrl">The URL of the web shop.</param>
            public Builder SetWebShopUrl(string webShopUrl) { _settings.webShopUrl = webShopUrl; return this; }

            /// <summary>
            /// Sets the redirect URL.
            /// </summary>
            /// <param name="redirectUrl">The URL to redirect to.</param>
            public Builder SetRedirectUrl(string redirectUrl) { _settings.redirectSettings.redirectUrl = redirectUrl; return this; }

            /// <summary>
            /// Sets whether to restore purchases on initialization (obsolete).
            /// </summary>
            /// <param name="restorePurchasesOnInit">True to restore purchases on init.</param>
            [Obsolete("Please use SetLocalPurchasesRestore instead.")]
            public Builder SetRestorePurchasesOnInit(bool restorePurchasesOnInit) { _settings.localPurchasesRestore = restorePurchasesOnInit; return this; }

            /// <summary>
            /// Sets whether to restore local purchases.
            /// </summary>
            /// <param name="localPurchaseRestore">True to restore local purchases.</param>
            public Builder SetLocalPurchasesRestore(bool localPurchaseRestore) { _settings.localPurchasesRestore = localPurchaseRestore; return this; }

            public Builder SetLocalPurchasesRestoreInterval(int intervalSec) { _settings.localPurchasesRestoreInterval = intervalSec; return this; }

            /// <summary>
            /// Sets whether restored multi-unit purchases are collapsed into a single purchase
            /// carrying the full quantity (<c>true</c>) or split into one single-unit purchase per
            /// owned unit (<c>false</c>, default). See <see cref="collapseRestoredMultiUnitPurchases"/>.
            /// <para/>
            /// Not supported on iOS yet (no effect there).
            /// </summary>
            public Builder SetCollapseRestoredMultiUnitPurchases(bool collapse) { _settings.collapseRestoredMultiUnitPurchases = collapse; return this; }
            
            /// <summary>
            /// Sets whether to use the buy button solution.
            /// </summary>
            /// <param name="useBuyButtonSolution">True to use the buy button solution.</param>
            public Builder SetUseBuyButtonSolution(bool useBuyButtonSolution = true) { _settings.useBuyButtonSolution = useBuyButtonSolution; return this; }

            /// <summary>
            /// Sets whether to opt-in into e-mail collection consent (e.g. for newsletter).
            /// </summary>
            /// <param name="emailCollectionConsentOptIn">True to opt-in into e-mail collection consent or `null` for defaults.</param>
            public Builder SetEmailCollectionConsentOptIn(bool? emailCollectionConsentOptIn) { _settings.emailCollectionConsentOptInEnabled = emailCollectionConsentOptIn; return this; }

            /// <summary>
            /// Sets the web view type.
            /// </summary>
            /// <param name="webViewType">The type of web view to use.</param>
            public Builder SetWebViewType(WebViewType webViewType) { _settings.webViewType = webViewType; return this; }

            /// <summary>
            /// Sets the web view orientation lock.
            /// </summary>
            /// <param name="webViewOrientationLock">The orientation lock for the web view.</param>
            public Builder SetWebViewOrientationLock(OrientationLock webViewOrientationLock) { _settings.webViewOrientationLock = webViewOrientationLock; return this; }

            /// <summary>
            /// Sets the file path for the web view splash screen image.
            /// <para/>
            /// For Android only.
            /// </summary>
            /// <param name="filepath">The file path to the splash screen image.</param>
            public Builder SetWebViewSplashScreenImage(string filepath) { _settings.webViewSplashScreenImageFilepath = filepath; return this; }

            /// <summary>
            /// Sets the drawable ID for the splash screen image on Android.
            /// <para/>
            /// For Android only.
            /// </summary>
            /// <param name="drawableId">The drawable ID for the splash screen image.</param>
            public Builder SetWebViewSplashScreenDrawableIdAndroid(int drawableId) { _settings.webViewSplashScreenImageDrawableIdAndroid = drawableId; return this; }

            /// <summary>
            /// Sets the redirect button text.
            /// </summary>
            /// <param name="redirectButtonText">The text for the redirect button.</param>
            public Builder SetRedirectButtonText(string redirectButtonText) { _settings.redirectSettings.redirectButtonText = redirectButtonText; return this; }

            /// <summary>
            /// Sets the redirect delay in seconds.
            /// </summary>
            /// <param name="redirectDelay">The delay before redirecting, in seconds.</param>
            public Builder SetRedirectDelay(int redirectDelay) { _settings.redirectSettings.redirectDelay = redirectDelay; return this; }

            /// <summary>
            /// Sets the UI theme size.
            /// </summary>
            /// <param name="themeSize">The theme size for the UI.</param>
            public Builder SetUIThemeSize(ThemeSize themeSize) { _settings.uiSettings.themeSize = themeSize; return this; }

            /// <summary>
            /// Sets the UI theme style.
            /// </summary>
            /// <param name="themeStyle">The theme style for the UI.</param>
            public Builder SetUIThemeStyle(ThemeStyle themeStyle) { _settings.uiSettings.themeStyle = themeStyle; return this; }

            /// <summary>
            /// Sets the custom theme name for the UI.
            /// </summary>
            /// <param name="customTheme">The custom theme name.</param>
            public Builder SetUICustomTheme(string customTheme) { _settings.uiSettings.customTheme = customTheme; return this; }

            /// <summary>
            /// Sets the close button option.
            /// </summary>
            /// <param name="closeButton">The close button visibility option.</param>
            public Builder SetUseCloseButton(CloseButton closeButton) { _settings.closeButton = closeButton; return this; }

            /// <summary>
            /// Sets the control tint color for the UI.
            /// </summary>
            /// <param name="controlTintColor">The control tint color.</param>
            public Builder SetUIControlTintColor(Color controlTintColor) { _settings.uiSettings.controlTintColor = ColorUtility.ToHtmlStringRGBA(controlTintColor); return this; }

            /// <summary>
            /// Sets the bar tint color for the UI.
            /// </summary>
            /// <param name="barTintColor">The bar tint color.</param>
            public Builder SetUIBarTintColor(Color barTintColor) { _settings.uiSettings.barTintColor = ColorUtility.ToHtmlStringRGBA(barTintColor); return this; }
            
            /// <summary>
            /// Sets the webhooks mode.
            /// </summary>
            /// <param name="webhooksMode">The webhooks mode setting.</param>
            public Builder SetWebhooksMode(WebhooksMode webhooksMode) { _settings.webhooksMode = webhooksMode; return this; }
            
            /// <summary>
            /// Sets the additional authentication data associated with the social provider's access token.
            /// </summary>
            /// <param name="socialProvider">The social provider data to set.</param>
            public Builder SetSocialProvider(SocialProvider? socialProvider)
            {
                _settings.socialProvider = socialProvider;
                return this;
            }
            
            /// <summary>
            /// Sets the logo visibility option for the UI.
            /// </summary>
            /// <param name="visible">Visibility option for logo.</param>
            /// <returns>The current <see cref="Builder"/> instance.</returns>
            public Builder SetVisibleLogo(VisibleLogo visible) { _settings.uiSettings.visibleLogo = visible; return this; }


            /// <summary>
            /// Sets <see cref="AdvancedSettingsAndroid"/> that holds various advanced Android-oriented settings.
            /// </summary>
            /// <para/>
            /// For Android only.
            /// <param name="advancedSettingsAndroid">Advanced settings.</param>
            public Builder SetAdvancedSettingsAndroid([CanBeNull] AdvancedSettingsAndroid advancedSettingsAndroid)
            {
                _settings.advancedSettingsAndroid = advancedSettingsAndroid;
                return this;
            }

            /// <summary>
            /// Builds and returns the <see cref="XsollaClientSettings"/> instance.
            /// </summary>
            public XsollaClientSettings Build() => _settings;

            /// <summary>
            /// Returns an empty <see cref="XsollaClientSettings"/> instance.
            /// </summary>
            public static XsollaClientSettings Empty() => Create().Build();
        }

        /// <summary>
        /// Serializes the settings to JSON.
        /// </summary>
        /// <returns>JSON string representation of the settings.</returns>
        string ToJson() => XsollaClientHelpers.ToJson(this);

        /// <summary>
        /// Returns the JSON string representation of the settings.
        /// </summary>
        public override string ToString() => ToJson();
    }
}
