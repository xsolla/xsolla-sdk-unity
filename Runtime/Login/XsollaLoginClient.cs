using System;
using Xsolla.SDK.Common;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Login
{
    /// <summary>
    /// Main entry point for performing login operations using the Xsolla SDK.
    /// Supports widget-based, silent login, token refresh, and logout via web view or internal SDK flow.
    /// </summary>
    public class XsollaLoginClient
    {
        private const string Tag = "XsollaLoginClient";

        /// <summary>
        /// Internal login implementation (platform-specific).
        /// </summary>
        private readonly IXsollaLoginClient _loginImpl = XsollaLoginClientFactory.Create();

        /// <summary>
        /// Deferred future holding the resolved client configuration or error.
        /// </summary>
        private readonly ISimpleFuture<XsollaClientConfiguration, string> _settingsFuture;

        /// <summary>
        /// Builder class for creating instances of <see cref="XsollaLoginClient"/>.
        /// </summary>
        public class Builder
        {
            private XsollaClientConfiguration _configuration;

            /// <summary>
            /// Creates a new instance of the builder.
            /// </summary>
            public static Builder Create() => new Builder();

            /// <summary>
            /// Sets the configuration object for the login client.
            /// </summary>
            /// <param name="configuration">The client configuration.</param>
            /// <returns>The builder instance for chaining.</returns>
            public Builder SetConfiguration(XsollaClientConfiguration configuration)
            {
                _configuration = configuration;
                return this;
            }

            /// <summary>
            /// Builds and returns the login client using the current configuration.
            /// </summary>
            public XsollaLoginClient Build() => new XsollaLoginClient(_configuration);
        }

        /// <summary>
        /// Private constructor. Use the <see cref="Builder"/> to create instances.
        /// Initializes internal logging, resolves async config if needed.
        /// </summary>
        /// <param name="configuration">Xsolla client configuration object.</param>
        private XsollaLoginClient(XsollaClientConfiguration configuration)
        {
            RunOnStartThread.Create();
            XsollaLogger.Apply(configuration);

            _settingsFuture = SimpleFuture.Create<XsollaClientConfiguration, string>(out var promise);

            if (configuration.delayedTask != null)
            {
                XsollaDelayedConfiguration.Resolve(configuration, RunOnStartThread.Run, promise, Tag);
            }
            else
            {
                promise.Complete(configuration);
            }
        }

        /// <summary>
        /// Gets the resolved client configuration if available.
        /// </summary>
        /// <returns>The <see cref="XsollaClientConfiguration"/> or null if not resolved yet.</returns>
        public XsollaClientConfiguration GetConfiguration()
        {
            if (_settingsFuture.TryGetValue(out var conf))
                return conf;
            return null;
        }

        /// <summary>
        /// Initiates login using the login widget.
        /// </summary>
        /// <param name="completionHandler">Callback invoked with token or error.</param>
        public void Login(Action<XsollaLoginToken, XsollaLoginClientError> completionHandler) =>
            _settingsFuture.OnComplete(
                onError: error => completionHandler?.Invoke(null, XsollaLoginClientError.Message(error)),
                onSuccess: configuration => Login(configuration, completionHandler)
            );

        /// <summary>
        /// Initiates silent login without showing the widget.
        /// </summary>
        /// <param name="completionHandler">Callback invoked with token or error.</param>
        public void LoginSilently(Action<XsollaLoginToken, XsollaLoginClientError> completionHandler) =>
            _settingsFuture.OnComplete(
                onError: error => completionHandler?.Invoke(null, XsollaLoginClientError.Message(error)),
                onSuccess: configuration => LoginSilently(configuration, completionHandler)
            );
        
        /// <summary>
        /// Logs in a user using a social account token from a specified provider.
        /// </summary>
        /// <param name="provider">The name of the social provider (e.g., Facebook, Google).</param>
        /// <param name="token">The authentication token received from the social provider.</param>
        /// <param name="completionHandler">Callback invoked with token or error.</param>
        public void LoginWithSocialAccount(string provider, string token, Action<XsollaLoginToken, XsollaLoginClientError> completionHandler) =>
            _settingsFuture.OnComplete(
                onError: error => completionHandler?.Invoke(null, XsollaLoginClientError.Message(error)),
                onSuccess: configuration => LoginWithSocialAccount(configuration, provider, token, completionHandler)
            );

        /// <summary>
        /// Performs login using the specified configuration.
        /// </summary>
        /// <param name="configuration">The login configuration.</param>
        /// <param name="completionHandler">Callback with login token or error.</param>
        public void Login(
            XsollaClientConfiguration configuration,
            Action<XsollaLoginToken, XsollaLoginClientError> completionHandler
        )
        {
            XsollaLogger.Apply(configuration);

            _loginImpl.Login(configuration,
                onSuccess: token => completionHandler?.Invoke(token, null),
                onError: error => completionHandler?.Invoke(null, XsollaLoginClientError.Message(error))
            );
        }

        /// <summary>
        /// Performs silent login using the specified configuration.
        /// </summary>
        /// <param name="configuration">The login configuration.</param>
        /// <param name="completionHandler">Callback with login token or error.</param>
        public void LoginSilently(
            XsollaClientConfiguration configuration,
            Action<XsollaLoginToken, XsollaLoginClientError> completionHandler
        )
        {
            XsollaLogger.Apply(configuration);

            _loginImpl.LoginSilently(configuration,
                onSuccess: token => completionHandler?.Invoke(token, null),
                onError: error => completionHandler?.Invoke(null, XsollaLoginClientError.Message(error))
            );
        }
        
        /// <summary>
        /// Performs login using a social account token from a specific provider,
        /// using the provided Xsolla client configuration.
        /// </summary>
        /// <param name="configuration">Client configuration containing settings like log level.</param>
        /// <param name="provider">The social provider name (e.g., "facebook", "google").</param>
        /// <param name="token">The authentication token obtained from the provider.</param>
        /// <param name="completionHandler">Callback invoked with token or error.</param>
        public void LoginWithSocialAccount(
            XsollaClientConfiguration configuration, string provider, string token,
            Action<XsollaLoginToken, XsollaLoginClientError> completionHandler
        )
        {
            XsollaLogger.Apply(configuration);

            _loginImpl.LoginWithSocialAccount(configuration, provider, token,
                onSuccess: token => completionHandler?.Invoke(token, null),
                onError: error => completionHandler?.Invoke(null, XsollaLoginClientError.Message(error))
            );
        }

        /// <summary>
        /// Signs the user out of the saved session using the default configuration.
        /// </summary>
        /// <remarks>
        /// On Android, it signs the user out of the Xsolla Login widget session and revokes its refresh token.
        /// A device ID or social session is kept: the native SDK cannot sign those out, and a device ID sign-in
        /// returns the same user anyway.
        /// On Win/Mac/WebGL, it discards the saved session and revokes its refresh token. A device ID session is kept:
        /// the store's purchases run on it, and a device ID sign-in returns the same user anyway.
        /// On both, a failed server revoke only logs a warning, and the user stays signed out.
        /// A store initialized with the session's access token keeps that token until you deinitialize the store
        /// or pass the next user's token to <c>UpdateAccessToken</c>.
        /// </remarks>
        /// <param name="completionHandler">Callback invoked when complete or on error.</param>
        public void ClearToken(Action<XsollaLoginClientError> completionHandler) =>
            _settingsFuture.OnComplete(
                onError: err => completionHandler?.Invoke(XsollaLoginClientError.Message(err)),
                onSuccess: conf => ClearToken(conf, completionHandler)
            );

        /// <summary>
        /// Signs the user out of the saved session using the specified configuration.
        /// </summary>
        /// <remarks>
        /// Same as <see cref="ClearToken(Action{XsollaLoginClientError})"/>, which says what each platform signs out of and which sessions it keeps.
        /// </remarks>
        /// <param name="configuration">The client configuration.</param>
        /// <param name="completionHandler">Callback invoked when complete or on error.</param>
        public void ClearToken(
            XsollaClientConfiguration configuration,
            Action<XsollaLoginClientError> completionHandler
        ) => _loginImpl.ClearToken(configuration,
                onSuccess: () => completionHandler?.Invoke(null),
                onError: error => completionHandler?.Invoke(XsollaLoginClientError.Message(error))
            );

        /// <summary>
        /// Refreshes the authentication token using the configuration's access token.
        /// </summary>
        /// <param name="completionHandler">Callback with new token or error.</param>
        public void RefreshToken(Action<XsollaLoginToken, XsollaLoginClientError> completionHandler) =>
            _settingsFuture.OnComplete(
                onError: err => completionHandler?.Invoke(null, XsollaLoginClientError.Message(err)),
                onSuccess: conf =>
                    RefreshToken(
                        configuration: conf,
                        token: new XsollaLoginToken(conf.accessToken, refreshToken: "", expirationDate: 0),
                        completionHandler
                    )
            );

        /// <summary>
        /// Refreshes the authentication token using the specified token.
        /// </summary>
        /// <param name="token">Existing token to refresh.</param>
        /// <param name="completionHandler">Callback with new token or error.</param>
        public void RefreshToken(
            XsollaLoginToken token,
            Action<XsollaLoginToken, XsollaLoginClientError> completionHandler
        ) => _settingsFuture.OnComplete(
                onError: err => completionHandler?.Invoke(null, XsollaLoginClientError.Message(err)),
                onSuccess: conf => RefreshToken(conf, token, completionHandler)
            );

        /// <summary>
        /// Refreshes the authentication token using a specific configuration and token.
        /// </summary>
        /// <param name="configuration">Client configuration to use.</param>
        /// <param name="token">Token to refresh.</param>
        /// <param name="completionHandler">Callback with new token or error.</param>
        public void RefreshToken(
            XsollaClientConfiguration configuration,
            XsollaLoginToken token,
            Action<XsollaLoginToken, XsollaLoginClientError> completionHandler
        ) => _loginImpl.RefreshToken(configuration, token,
                onSuccess: token => completionHandler?.Invoke(token, null),
                onError: error => completionHandler?.Invoke(null, XsollaLoginClientError.Message(error))
            );
    }
}
