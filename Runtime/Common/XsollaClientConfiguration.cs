using JetBrains.Annotations;
using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Common
{
    /// <summary>
    /// Represents the client configuration for Xsolla SDK.
    /// </summary>
    [Serializable]
    public class XsollaClientConfiguration
    {
        const string TAG = "XsollaClientConfiguration";
        
        /// <summary>
        /// Specifies the available simple modes.
        /// </summary>
        public enum SimpleMode
        {
            /// <summary>Simple mode is off.</summary>
            Off,
            /// <summary>WebShop simple mode.</summary>
            WebShop,
            /// <summary>ServerTokens simple mode.</summary>
            ServerTokens
        }

        /// <summary>
        /// Delegate for delayed configuration.
        /// </summary>
        /// <param name="configuration">The configuration to be delayed.</param>
        /// <returns>The delayed configuration.</returns>
        public delegate XsollaClientConfiguration OnDelayedConfiguration(XsollaClientConfiguration configuration);

        /// <summary>Client settings instance.</summary>
        public XsollaClientSettings settings = XsollaClientSettings.Builder.Empty();

        /// <summary>Access token for authentication.</summary>
        public string accessToken = string.Empty;

        /// <summary>Indicates whether sandbox mode is enabled.</summary>
        public bool sandbox = false;

        /// <summary>Log level for the SDK.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public XsollaLogLevel logLevel = XsollaLogLevel.None;

        /// <summary>Task for delayed configuration.</summary>
        [NonSerialized]
        public Task<OnDelayedConfiguration> delayedTask = null;

        /// <summary>Locale string.</summary>
        public string locale = string.Empty;

        /// <summary>Fallback to default locale if not set.</summary>
        public bool fallbackToDefaultLocaleIfNotSet = false;

        /// <summary>Fetch products with geo locale.</summary>
        public bool fetchProductsWithGeoLocale = false;

        /// <summary>User ID.</summary>
        public string userId = string.Empty;

        /// <summary>Simple mode setting.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public SimpleMode simpleMode = SimpleMode.Off;

        /// <summary>Tracking ID.</summary>
        public string trackingId = string.Empty;

        public bool fetchPersonalizedProductsOnly = true;

        public string customPayStationDomainProduction = null;
        public string customPayStationDomainSandbox = null;

        /// <summary>SDK name.</summary>
        public readonly string sdkName = "unity";

        /// <summary>SDK version.</summary>
        public readonly string sdkVersion = Application.unityVersion;

        /// <summary>
        /// Protected constructor to prevent direct instantiation.
        /// </summary>
        protected XsollaClientConfiguration() {}

        /// <summary>
        /// Builder class for constructing <see cref="XsollaClientConfiguration"/> instances.
        /// </summary>
        public class Builder
        {
            /// <summary>Internal configuration instance.</summary>
            protected XsollaClientConfiguration _configuration = new XsollaClientConfiguration();

            /// <summary>
            /// Creates a new builder instance.
            /// </summary>
            public static Builder Create() => new Builder();

            /// <summary>
            /// Updates the builder with an existing configuration instance.
            /// </summary>
            /// <param name="configuration">Existing configuration to update.</param>
            public static Builder Update(XsollaClientConfiguration configuration)
            {
                var builder = new Builder();
                builder._configuration = configuration;
                return builder;
            }

            /// <summary>
            /// Sets the client settings.
            /// </summary>
            /// <param name="settings">The client settings.</param>
            public Builder SetSettings(XsollaClientSettings settings) { _configuration.settings = settings; return this; }

            /// <summary>
            /// Sets the access token.
            /// </summary>
            /// <param name="accessToken">The access token for authentication.</param>
            public Builder SetAccessToken(string accessToken) { _configuration.accessToken = accessToken; return this; }

            /// <summary>
            /// Sets sandbox mode.
            /// </summary>
            /// <param name="sandbox">True to enable sandbox mode.</param>
            public Builder SetSandbox(bool sandbox) { _configuration.sandbox = sandbox; return this; }

            /// <summary>
            /// Sets the log level.
            /// </summary>
            /// <param name="logLevel">The log level for the SDK.</param>
            public Builder SetLogLevel(XsollaLogLevel logLevel) { _configuration.logLevel = logLevel; return this; }

            /// <summary>
            /// Sets the delayed configuration task.
            /// </summary>
            /// <param name="task">The delayed configuration task.</param>
            public Builder SetDelayedConfigurationTask(Task<OnDelayedConfiguration> task) { _configuration.delayedTask = task; return this; }

            /// <summary>
            /// Sets the locale.
            /// </summary>
            /// <param name="locale">The locale string.</param>
            public Builder SetLocale(string locale) { _configuration.locale = locale; return this; }

            /// <summary>
            /// Sets fallback to default locale if not set.
            /// </summary>
            /// <param name="fallbackToDefaultLocaleIfNotSet">True to fallback to default locale.</param>
            public Builder SetFallbackToDefaultLocaleIfNotSet(bool fallbackToDefaultLocaleIfNotSet)
            {
                _configuration.fallbackToDefaultLocaleIfNotSet = fallbackToDefaultLocaleIfNotSet;
                return this;
            }

            /// <summary>
            /// Sets fetch products with geo locale.
            /// </summary>
            /// <param name="fetchProductsWithGeoLocale">True to fetch products with geo locale.</param>
            public Builder SetFetchProductsWithGeoLocale(bool fetchProductsWithGeoLocale)
            {
                _configuration.fetchProductsWithGeoLocale = fetchProductsWithGeoLocale;
                return this;
            }

            /// <summary>
            /// Sets the user ID.
            /// </summary>
            /// <param name="userId">The user ID.</param>
            public Builder SetUserId(string userId) { _configuration.userId = userId; return this; }

            /// <summary>
            /// Sets the tracking ID.
            /// </summary>
            /// <param name="trackingId">The tracking ID.</param>
            public Builder SetTrackingId(string trackingId) { _configuration.trackingId = trackingId; return this; }

            /// <summary>
            /// Sets the simple mode.
            /// </summary>
            /// <param name="simpleMode">The simple mode setting.</param>
            public Builder SetSimpleMode(SimpleMode simpleMode) { _configuration.simpleMode = simpleMode; return this; }

            /// <summary>
            /// Determines whether only personalized products are fetched from the catalog.
            ///
            /// When <c>true</c>, any item with a per-user purchase limit that has already been reached
            /// will be excluded from the fetched catalog (handled by the backend).
            ///
            /// When <c>false</c>, all items are fetched, including those whose limits have been reached.
            /// </summary>
            public Builder SetFetchPersonalizedProductsOnly(bool fetchPersonalizedProductsOnly)
            {
                _configuration.fetchPersonalizedProductsOnly = fetchPersonalizedProductsOnly;
                return this;
            }
            
            /// <summary>
            /// Overrides the Pay Station domain used to open the payment UI for both environments.
            ///
            /// By default the SDK uses Xsolla's hosted Pay Station URLs:
            /// <list type="bullet">
            ///   <item><description>Production — <c>https://secure.xsolla.com</c></description></item>
            ///   <item><description>Sandbox    — <c>https://sandbox-secure.xsolla.com</c></description></item>
            /// </list>
            ///
            /// Pass <c>null</c> or an empty string for either argument to keep the corresponding
            /// environment's default domain unchanged.
            ///
            /// The value must be an absolute URL, e.g. <c>https://pay.example.com</c>.
            /// Query strings and paths are not allowed — supply the scheme and host only.
            /// </summary>
            /// <param name="domainProduction">Custom domain for the live (production) environment.</param>
            /// <param name="domainSandbox">Custom domain for the sandbox (test) environment.</param>
            public Builder SetCustomPayStationDomain(string domainProduction, string domainSandbox)
            {
                if (!isValidDomain(domainProduction))
                    XsollaLogger.Error(TAG, "Invalid production domain format.");
                if (!isValidDomain(domainSandbox))
                    XsollaLogger.Error(TAG, "Invalid sandbox domain format.");

                _configuration.customPayStationDomainProduction = isValidDomain(domainProduction) ? domainProduction : string.Empty;
                _configuration.customPayStationDomainSandbox = isValidDomain(domainSandbox) ? domainSandbox : string.Empty;
                return this;

                static bool isValidDomain(string domain)
                {
                    if (string.IsNullOrEmpty(domain))
                        return true;

                    return Uri.IsWellFormedUriString(domain, UriKind.Absolute);
                }
            }

            /// <summary>
            /// Overrides the Pay Station domain for the live (production) environment only.
            /// The sandbox domain is left unchanged.
            /// <para>See <see cref="SetCustomPayStationDomain(string,string)"/> for format details.</para>
            /// </summary>
            /// <param name="domainProduction">Custom domain for the live (production) environment.</param>
            public Builder SetCustomPayStationProductionDomain(string domainProduction)
                => SetCustomPayStationDomain(domainProduction, _configuration.customPayStationDomainSandbox);

            /// <summary>
            /// Overrides the Pay Station domain for the sandbox (test) environment only.
            /// The production domain is left unchanged.
            /// <para>See <see cref="SetCustomPayStationDomain(string,string)"/> for format details.</para>
            /// </summary>
            /// <param name="domainSandbox">Custom domain for the sandbox (test) environment.</param>
            public Builder SetCustomPayStationSandboxDomain(string domainSandbox)
                => SetCustomPayStationDomain(_configuration.customPayStationDomainProduction, domainSandbox);

            /// <summary>
            /// Builds and returns the <see cref="XsollaClientConfiguration"/> instance.
            /// </summary>
            public XsollaClientConfiguration Build() => _configuration;

            /// <summary>
            /// Returns an empty <see cref="XsollaClientConfiguration"/> instance.
            /// </summary>
            public static XsollaClientConfiguration Empty() => Create().Build();
        }

        /// <summary>
        /// Serializes the configuration to JSON.
        /// </summary>
        /// <returns>JSON string representation of the configuration.</returns>
        string ToJson() => XsollaClientHelpers.ToJson(this);

        /// <summary>
        /// Returns the JSON string representation of the configuration.
        /// </summary>
        public override string ToString() => ToJson();
    }

    /// <summary>
    /// Extension methods for <see cref="XsollaClientConfiguration"/>.
    /// </summary>
    public static class XsollaClientConfigurationExtensions
    {
        /// <summary>
        /// Returns current locale based on the settings in the configuration.
        /// </summary>
        /// <param name="config">The client configuration.</param>
        /// <returns>The current <see cref="LocaleInfo"/> or null.</returns>
        [CanBeNull]
        public static LocaleInfo GetCurrentLocale(this XsollaClientConfiguration config) =>
          string.IsNullOrEmpty(config.locale)
            ? config.fallbackToDefaultLocaleIfNotSet ? LocaleInfo.Default : null
            : LocaleInfo.CreateLocaleInfo(config.locale);
    }
}
