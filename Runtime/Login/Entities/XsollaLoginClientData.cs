using System;
using Xsolla.SDK.Common;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Login
{
    /// <summary>
    /// Represents an error returned by the Xsolla Login Client.
    /// </summary>
    [Serializable]
    public class XsollaLoginClientError
    {
        /// <summary>
        /// The error message.
        /// </summary>
        public string message;

        /// <summary>
        /// Constructs an error with the provided message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public XsollaLoginClientError(string message)
        {
            this.message = message;
        }

        /// <summary>
        /// Static factory method to create an error instance with a given message.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <returns>A new instance of <see cref="XsollaLoginClientError"/>.</returns>
        public static XsollaLoginClientError Message(string message) => new XsollaLoginClientError(message);

        /// <summary>
        /// Returns the error message as a string.
        /// </summary>
        /// <returns>The error message.</returns>
        public override string ToString() => message;
    }

    /// <summary>
    /// Holds the token information for the logged-in user.
    /// </summary>
    [Serializable]
    public class XsollaLoginToken
    {
        /// <summary>
        /// The access token string.
        /// </summary>
        public string accessToken;

        /// <summary>
        /// The refresh token string.
        /// </summary>
        public string refreshToken;

        /// <summary>
        /// Expiration date of the access token, represented as a Unix timestamp (seconds).
        /// </summary>
        public long expirationDate;

        /// <summary>
        /// Gets an empty instance of <see cref="XsollaLoginToken"/>.
        /// </summary>
        public static XsollaLoginToken Empty => new XsollaLoginToken("", "", 0);

        /// <summary>
        /// Default constructor for serialization.
        /// </summary>
        public XsollaLoginToken() { }

        /// <summary>
        /// Constructs a token instance with access token, refresh token, and expiration.
        /// </summary>
        /// <param name="accessToken">Access token string.</param>
        /// <param name="refreshToken">Refresh token string.</param>
        /// <param name="expirationDate">Unix timestamp of expiration.</param>
        public XsollaLoginToken(string accessToken, string refreshToken, long expirationDate)
        {
            this.accessToken = accessToken;
            this.refreshToken = refreshToken;
            this.expirationDate = expirationDate;
        }

        /// <summary>
        /// Constructs a token instance with access token.
        /// </summary>
        /// <param name="accessToken">Access token string.</param>
        public XsollaLoginToken(string accessToken) : this(
            accessToken, refreshToken: string.Empty, expirationDate: 0
        ) {}

        /// <summary>
        /// Returns a string representation of the token.
        /// </summary>
        /// <returns>A formatted string with token data.</returns>
        /// <remarks>
        /// With secret obfuscation on, it obfuscates both tokens.
        /// </remarks>
        public override string ToString() =>
            $"XsollaLoginToken: AccessToken={LogObfuscation.ObfuscateIfEnabled(accessToken)}, " +
            $"RefreshToken={LogObfuscation.ObfuscateIfEnabled(refreshToken)}, ExpirationDate={expirationDate}";

        /// <summary>
        /// Gets the number of seconds remaining before the token expires.
        /// Returns 0 if expiration date is not set.
        /// </summary>
        public long expiresIn => expirationDate > 0
            ? expirationDate - DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            : 0;
    }

    /// <summary>
    /// Provides access to Xsolla Login-specific settings using the common settings asset.
    /// </summary>
    public class XsollaLoginClientSettingsAsset : XsollaClientSettingsAsset
    {
        /// <summary>
        /// Gets an instance of the client settings asset.
        /// </summary>
        public new static XsollaClientSettingsAsset Instance() => XsollaClientSettingsAsset.Instance();
    }

    /// <summary>
    /// Extends base client settings with login-specific builders.
    /// </summary>
    [Serializable]
    public class XsollaLoginClientSettings : XsollaClientSettings
    {
        /// <summary>
        /// Builder class for constructing <see cref="XsollaLoginClientSettings"/>.
        /// </summary>
        public new class Builder : XsollaClientSettings.Builder
        {
            /// <summary>
            /// Creates a new builder instance.
            /// </summary>
            public new static Builder Create() => new Builder();

            /// <summary>
            /// Updates the builder with an existing settings instance.
            /// </summary>
            /// <param name="settings">Settings to apply to the builder.</param>
            /// <returns>The updated builder instance.</returns>
            public new static Builder Update(XsollaClientSettings settings)
            {
                var builder = new Builder();
                builder._settings = settings;
                return builder;
            }
        }
    }

    /// <summary>
    /// Extends base client configuration with login-specific builders.
    /// </summary>
    [Serializable]
    public class XsollaLoginClientConfiguration : XsollaClientConfiguration
    {
        /// <summary>
        /// Builder class for constructing <see cref="XsollaLoginClientConfiguration"/>.
        /// </summary>
        public new class Builder : XsollaClientConfiguration.Builder
        {
            /// <summary>
            /// Creates a new builder instance.
            /// </summary>
            public new static Builder Create() => new Builder();

            /// <summary>
            /// Updates the builder with an existing configuration instance.
            /// </summary>
            /// <param name="configuration">Configuration to apply.</param>
            /// <returns>The updated builder instance.</returns>
            public new static Builder Update(XsollaClientConfiguration configuration)
            {
                var builder = new Builder();
                builder._configuration = configuration;
                return builder;
            }
        }
    }

    /// <summary>
    /// Contains the URL that triggers WebView dismissal in the login flow.
    /// </summary>
    [Serializable]
    public class XsollaLoginClientWebViewDismissUrl
    {
        /// <summary>
        /// The URL used to close the WebView.
        /// </summary>
        public string url;
    }

    /// <summary>
    /// Contains authentication token details returned by the login process.
    /// </summary>
    [Serializable]
    public class XsollaLoginClientTokenInfo
    {
        /// <summary>
        /// The access token string.
        /// </summary>
        public string accessToken;

        /// <summary>
        /// The refresh token string.
        /// </summary>
        public string refreshToken;

        /// <summary>
        /// The number of seconds until the token expires.
        /// </summary>
        public int expiresIn;
    }
}
