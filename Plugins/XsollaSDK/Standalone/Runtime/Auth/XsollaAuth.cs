using System;
using System.Collections.Generic;
using UnityEngine;
using Xsolla.Core;

namespace Xsolla.Auth
{
    internal static class XsollaAuth
    {
        private const string BASE_URL = "https://login.xsolla.com/api";

        /// <summary>
        /// Checks if the user is authenticated. Returns `true` if the token exists and the user is authenticated.
        /// </summary>
        public static bool IsUserAuthenticated(XsollaSettings settings)
        {
            return settings.XsollaToken.Exists;
        }

        /// <summary>
        /// <summary>
        /// Authenticates the user with the access token using social network credentials.
        /// </summary>
        /// <param name="accessToken">Access token received from a social network.</param>
        /// <param name="accessTokenSecret">Parameter `oauth_token_secret` received from the authorization request. Required for Twitter only.</param>
        /// <param name="openId">Parameter `openid` received from the social network. Required for WeChat only.</param>
        /// <param name="provider">Name of the social network connected to Login in Publisher Account. Can be `facebook`, `google`, `linkedin`, `twitter`, `discord`, `naver`, `baidu`, `wechat`, or `qq_mobile`.</param>
        /// <param name="onSuccess">Called after successful user authentication on the specified platform.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        /// <param name="redirectUri">URI to redirect the user to after account confirmation, successful authentication, two-factor authentication configuration, or password reset confirmation.
        ///     Must be identical to the OAuth 2.0 redirect URIs specified in Publisher Account.
        ///     Required if there are several URIs.</param>
        /// <param name="state">Value used for additional user verification on backend. Must be at least 8 symbols long. `xsollatest` by default. Required for OAuth 2.0.</param>
        public static void AuthWithSocialNetworkAccessToken(XsollaSettings settings, string accessToken, string accessTokenSecret, string openId, string provider, Action onSuccess, Action<Error> onError, string redirectUri = null, string state = null)
        {
            if (settings.OAuthClientId != -1 && settings.OAuthClientId != 0) {
                ExchangeSocialTokenForOAuth2(settings, provider, accessToken, accessTokenSecret, openId, isBasedOnDeviceId: false, onSuccess, onError, redirectUri, state);
            } else {
                var url = new UrlBuilder(BASE_URL + $"/social/{provider}/login_with_token")
                    .AddProjectId(settings.LoginId)
                    .Build();

                var requestData = new AuthWithSocialNetworkAccessTokenRequest
                {
                    access_token = accessToken,
                    access_token_secret = accessTokenSecret,
                    openId = openId
                };

                WebRequestHelper.Instance.PostRequest<TokenResponseSimple, AuthWithSocialNetworkAccessTokenRequest>(
                    SdkType.Login,
                    url,
                    requestData,
                    onComplete: response =>
                    {
                        settings.XsollaToken.Create(response.token, isBasedOnDeviceId: false);
                        onSuccess?.Invoke();
                    },
                    onError,
                    ErrorGroup.LoginErrors);
            }
        }

        /// Authenticates user with the saved token.
        /// <param name="onSuccess">Called after successful authentication.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        /// </summary>
        public static void AuthBySavedToken(XsollaSettings settings, Action onSuccess, Action<Error> onError)
        {
            if (!settings.XsollaToken.TryLoadInstance()) {
                var error = new Error(errorMessage: "Failed to auth via saved token");
                XDebug.Log(settings, $"XsollaAuth.AuthBySavedToken: no saved token found; {error}");
                onError?.Invoke(error);

                return;
            }

            PopulateMissingExpiryFromJwt(settings);

            var refreshTokenExists = !string.IsNullOrEmpty(settings.XsollaToken.RefreshToken);

            if (!settings.XsollaToken.IsExpired) {
                XDebug.Log(settings, $"XsollaAuth.AuthBySavedToken: saved token is still valid (expirationTime={settings.XsollaToken.ExpirationTime}, hasRefreshToken={refreshTokenExists}); checking embedded social claims");
                TokenAutoRefresher.CheckAndRefreshSocialIfNeeded(settings, onSuccess, onError);

                return;
            }

            if (refreshTokenExists) {
                XDebug.Log(settings, "XsollaAuth.AuthBySavedToken: saved token is expired and a refresh token is available; refreshing via OAuth2");

                RefreshToken(settings,
                    onSuccess: () =>
                    {
                        XDebug.Log(settings, "XsollaAuth.AuthBySavedToken: OAuth2 refresh succeeded; checking embedded social claims on the new token");
                        TokenAutoRefresher.CheckAndRefreshSocialIfNeeded(settings, onSuccess, onError);
                    },
                    onError: e =>
                    {
                        XDebug.LogWarning(settings, $"XsollaAuth.AuthBySavedToken: OAuth2 refresh failed ({e}); attempting claims-decay or silent re-auth fallback");
                        TokenAutoRefresher.TryClaimsDecayOrReauth(settings, onSuccess, onError);
                    });

                return;
            }

            XDebug.Log(settings, "XsollaAuth.AuthBySavedToken: saved token is expired and no refresh token is available; attempting claims-decay or silent re-auth fallback");
            TokenAutoRefresher.TryClaimsDecayOrReauth(settings, onSuccess, onError);
        }

        // Hydrates the in-memory token's expiry from its JWT `exp` claim when the
        // stored ExpirationTime is missing (e.g. token came from a non-OAuth
        // endpoint that doesn't return `expires_in`). Without this, IsExpired
        // returns false for such tokens and the proactive refresh paths never fire.
        private static void PopulateMissingExpiryFromJwt(XsollaSettings settings)
        {
            if (settings.XsollaToken.ExpirationTime > 0)
                return;

            if (!JwtUtils.TryDecodeExp(settings.XsollaToken.AccessToken, out var expirationEpochSeconds)) {
                XDebug.Log(settings, "XsollaAuth.PopulateMissingExpiryFromJwt: token has no stored expiry and JWT exp claim could not be decoded; treating as never-expiring");

                return;
            }

            var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            XDebug.Log(settings, $"XsollaAuth.PopulateMissingExpiryFromJwt: hydrating expiry from JWT exp claim (epoch={expirationEpochSeconds}, relative={expirationEpochSeconds - nowEpoch}s)");
            XDebug.LogDebug(settings, $"XsollaAuth.PopulateMissingExpiryFromJwt: source access token = {settings.XsollaToken.AccessToken}");

            settings.XsollaToken.CreateWithEpochExpiration(
                settings.XsollaToken.AccessToken,
                settings.XsollaToken.RefreshToken,
                expirationEpochSeconds,
                settings.XsollaToken.IsBasedOnDeviceId
            );
        }

        /// <summary>
        /// Authenticates the user with Xsolla Login widget.
        /// For standalone builds, the widget opens in the built-in browser that is included with the SDK.
        /// </summary>
        /// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/authentication/login-widget/).</remarks>
        /// <param name="onSuccess">Called after successful authentication.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        /// <param name="onCancel">Called after browser closing by user.</param>
        /// <param name="locale">Login widget UI language. Supported languages: Arabic (ar_AE), Bulgarian (bg_BG), Czech (cz_CZ), Filipino (fil-PH), English (en_XX), German (de_DE), Spanish (es_ES), French (fr_FR), Hebrew (he_IL), Indonesian (id-ID), Italian (it_IT), Japanese (ja_JP), Khmer (km-KH), Korean (ko_KR), Lao language ( lo-LA), Myanmar (my-MM), NepaliPolish (ne-NP), (pl_PL), Portuguese (pt_BR), Romanian (ro_RO), Russian (ru_RU), Thai (th_TH), Turkish (tr_TR), Vietnamese (vi_VN), Chinese Simplified (zh_CN), Chinese Traditional (zh_TW).</param>
        /// <param name="sdkType">SDK type. Used for internal analytics.</param>
        public static void AuthWithXsollaWidget(XsollaSettings settings, Action onSuccess, Action<Error> onError, Action onCancel, string locale = null, SdkType sdkType = SdkType.Login)
        {
            var authenticator = new WidgetAuthenticatorFactory().Create(settings, onSuccess, onError, onCancel, locale, sdkType);

            if (authenticator != null)
                authenticator.Launch();
            else
                onError?.Invoke(new Error(ErrorType.NotSupportedOnCurrentPlatform, errorMessage: $"Auth with Xsolla Widget is not supported for this platform: {Application.platform}"));
        }

        /// <summary>
        /// Authenticates the user via Xsolla Launcher
        /// </summary>
        /// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/authentication/auth-via-launcher/#unity_sdk_how_to_set_up_auth_via_launcher).</remarks>
        /// <param name="onSuccess">Called after successful authentication.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        public static void AuthViaXsollaLauncher(XsollaSettings settings, Action onSuccess, Action<Error> onError)
        {
            new XsollaLauncherAuth().Perform(settings, onSuccess, onError);
        }

        /// <summary>
        /// Logs the user out and deletes the user session according to the value of the sessions parameter (OAuth2.0 only).
        /// </summary>
        /// <param name="onSuccess">Called after successful user logout.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        /// <param name="logoutType">Shows how the user is logged out and how the user session is deleted. Can be `sso` or `all` (default). Leave empty to use the default value.</param>
        public static void Logout(XsollaSettings settings, Action onSuccess, Action<Error> onError, LogoutType logoutType = LogoutType.All)
        {
            var url = new UrlBuilder(BASE_URL + "/oauth2/logout")
                .AddParam("sessions", logoutType.ToString().ToLowerInvariant())
                .Build();

            WebRequestHelper.Instance.GetRequest(
                SdkType.Login,
                url,
                WebRequestHeader.AuthHeader(settings),
                onSuccess,
                onError);

            settings.XsollaToken.DeleteSavedInstance();
        }

        /// <summary>
        /// Authenticates the user via a particular device ID.
        /// </summary>
        /// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/authentication/auth-via-device-id/).</remarks>
        /// <param name="onSuccess">Called after successful user authentication via the device ID.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        /// <param name="deviceInfo">Information about the device that is used to identify the user. if not specified, the method defines this infotmation automatically.</param>
        /// <param name="redirectUri">URI to redirect the user to after account confirmation, successful authentication, two-factor authentication configuration, or password reset confirmation.
        ///     Must be identical to the OAuth 2.0 redirect URIs specified in Publisher Account.
        ///     Required if there are several URIs.</param>
        /// <param name="state">Value used for additional user verification on backend. Must be at least 8 symbols long. `xsollatest` by default. Required for OAuth 2.0.</param>
        public static void AuthViaDeviceID(XsollaSettings settings, Action onSuccess, Action<Error> onError, DeviceInfo deviceInfo = null, string redirectUri = null, string state = null)
        {
            if (DataProvider.GetPlatform() == RuntimePlatform.WebGLPlayer) {
                onError?.Invoke(new Error(ErrorType.NotSupportedOnCurrentPlatform, errorMessage: $"Auth via Device ID is not supported for this platform: {Application.platform}"));

                return;
            }

            if (deviceInfo == null)
                deviceInfo = DeviceInfo.Create();

            var deviceType = deviceInfo.GetDeviceType();

            var requestData = new AuthViaDeviceIdRequest
            {
                device = deviceInfo.GetSafeDeviceData(),
                device_id = deviceInfo.GetSafeDeviceId()
            };

            if (settings.OAuthClientId != -1 && settings.OAuthClientId != 0) {
                var url = new UrlBuilder(BASE_URL + $"/oauth2/login/device/{deviceType}")
                    .AddClientId(settings.OAuthClientId)
                    .AddResponseType(GetResponseType())
                    .AddState(GetState(state))
                    .AddRedirectUri(RedirectUrlHelper.GetRedirectUrl(settings, redirectUri))
                    .AddScope(GetScope())
                    .Build();

                WebRequestHelper.Instance.PostRequest<LoginLink, AuthViaDeviceIdRequest>(
                    SdkType.Login,
                    url,
                    requestData,
                    onComplete: response =>
                        ParseCodeFromUrlAndExchangeToToken(settings, response.login_url, isBasedOnDeviceId: true, onSuccess, onError),
                    onError,
                    ErrorGroup.LoginErrors
                );
            } else {
                var url = new UrlBuilder(BASE_URL + $"/login/device/{deviceType}")
                    .AddProjectId(settings.LoginId)
                    .AddWithLogout("0")
                    //.AddPayload("")
                    .Build();

                WebRequestHelper.Instance.PostRequest<TokenResponseSimple, AuthViaDeviceIdRequest>(
                    SdkType.Login,
                    url,
                    requestData,
                    onComplete: response =>
                    {
                        settings.XsollaToken.Create(response.token, isBasedOnDeviceId: true);
                        onSuccess?.Invoke();
                    },
                    error => onError?.Invoke(error)
                );
            }
        }

        /// <summary>
        /// Authenticates a user by exchanging the session ticket from Steam, Xbox, or Epic Games to the JWT.
        /// </summary>
        /// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/authentication/silent-auth/).</remarks>
        /// <param name="providerName">Platform on which the session ticket was obtained. Can be `steam`, `xbox`, or `epicgames`.</param>
        /// <param name="appId">Platform application identifier.</param>
        /// <param name="sessionTicket">Session ticket received from the platform.</param>
        /// <param name="onSuccess">Called after successful user authentication with a platform session ticket. Authentication data including a JWT will be received.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        /// <param name="redirectUri">URI to redirect the user to after account confirmation, successful authentication, two-factor authentication configuration, or password reset confirmation.
        ///     Must be identical to the OAuth 2.0 redirect URIs specified in Publisher Account.
        ///     Required if there are several URIs.</param>
        /// <param name="state">Value used for additional user verification on backend. Must be at least 8 symbols long. Will be `xsollatest` by default. Used only for OAuth2.0 auth.</param>
        /// <param name="code">Code received from the platform.</param>
        public static void SilentAuth(XsollaSettings settings, string providerName, string appId, string sessionTicket, Action onSuccess, Action<Error> onError, string redirectUri = null, string state = null, string code = null)
        {
            var url = new UrlBuilder(BASE_URL + $"/oauth2/social/{providerName}/cross_auth")
                .AddClientId(settings.OAuthClientId)
                .AddResponseType(GetResponseType())
                .AddState(GetState(state))
                .AddRedirectUri(RedirectUrlHelper.GetRedirectUrl(settings, redirectUri))
                .AddScope(GetScope())
                .AddParam("app_id", appId)
                .AddParam("session_ticket", sessionTicket)
                .AddParam("code", code)
                .AddParam("is_redirect", "false")
                .Build();

            WebRequestHelper.Instance.GetRequest<LoginLink>(
                SdkType.Login,
                url,
                response => ParseCodeFromUrlAndExchangeToToken(settings, response.login_url, isBasedOnDeviceId: false, onSuccess, onError),
                onError);
        }

        /// <summary>
        /// Returns URL for authentication via the specified social network in a browser.
        /// </summary>
        /// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/authentication/social-auth/#sdk_how_to_set_up_web_auth_via_social_networks).</remarks>
        /// <param name="provider">Name of a social network. Provider must be connected to Login in Publisher Account.
        /// Can be `amazon`, `apple`, `baidu`, `battlenet`, `discord`, `facebook`, `github`, `google`, `kakao`, `linkedin`, `mailru`, `microsoft`, `msn`, `naver`, `ok`, `paypal`, `psn`, `qq`, `reddit`, `steam`, `twitch`, `twitter`, `vimeo`, `vk`, `wechat`, `weibo`, `yahoo`, `yandex`, `youtube`, or `xbox`.</param>
        /// <param name="redirectUri">URI to redirect the user to after account confirmation, successful authentication, two-factor authentication configuration, or password reset confirmation.
        ///     Must be identical to the OAuth 2.0 redirect URIs specified in Publisher Account.
        ///     Required if there are several URIs.</param>
        /// <param name="state">Value used for additional user verification on backend. Must be at least 8 symbols long. `xsollatest` by default. Required for OAuth 2.0.</param>
        public static string GetSocialNetworkAuthUrl(XsollaSettings settings, SocialProvider provider, string redirectUri = null, string state = null)
        {
            var providerValue = provider.ToApiParameter();

            var url = new UrlBuilder(BASE_URL + $"/oauth2/social/{providerValue}/login_redirect")
                .AddClientId(settings.OAuthClientId)
                .AddState(GetState(state))
                .AddResponseType(GetResponseType())
                .AddRedirectUri(RedirectUrlHelper.GetRedirectUrl(settings, redirectUri))
                .AddScope(GetScope())
                .Build();

            return WebRequestHelper.Instance.AppendAnalyticsToUrl(SdkType.Login, url);
        }

        /// <summary>
        /// Returns list of links for social authentication enabled in Publisher Account (<b>your Login project > Authentication > Social login</b> section).
        /// The links are valid for 10 minutes.
        /// You can get the link by this call and add it to your button for authentication via the social network.
        /// </summary>
        /// <param name="onSuccess">Called after list of links for social authentication was successfully received.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        /// <param name="locale"> Region in the `language code_country code` format, where:
        ///     - `language code` � language code in the [ISO 639-1](https://en.wikipedia.org/wiki/List_of_ISO_639-1_codes) format;
        ///     - `country code` � country/region code in the [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2) format.<br/>
        ///     The list of the links will be sorted from most to least used social networks, according to the variable value.
        /// </param>
        public static void GetLinksForSocialAuth(XsollaSettings settings, Action<SocialNetworkLinks> onSuccess, Action<Error> onError, string locale = null)
        {
            var url = new UrlBuilder(BASE_URL + "/users/me/login_urls")
                .AddLocale(locale)
                .Build();

            WebRequestHelper.Instance.GetRequest<List<SocialNetworkLink>>(
                SdkType.Login,
                url,
                WebRequestHeader.AuthHeader(settings),
                list =>
                {
                    onSuccess?.Invoke(new SocialNetworkLinks
                    {
                        items = list
                    });
                },
                error => TokenAutoRefresher.Check(settings, error, onError, () => GetLinksForSocialAuth(settings, onSuccess, onError, locale)));
        }

        /// <summary>
        /// Refreshes the token in case it is expired. Works only when OAuth 2.0 is enabled.
        /// </summary>
        /// <param name="onSuccess"> Called after successful token refreshing. Refresh data including the JWT will be received.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        /// <param name="redirectUri">URI to redirect the user to after account confirmation, successful authentication, two-factor authentication configuration, or password reset confirmation.
        ///     Must be identical to the OAuth 2.0 redirect URIs specified in Publisher Account.
        ///     Required if there are several URIs.</param>
        public static void RefreshToken(XsollaSettings settings, Action onSuccess, Action<Error> onError, string redirectUri = null)
        {
            var refreshToken = settings.XsollaToken.RefreshToken;

            if (string.IsNullOrEmpty(refreshToken)) {
                onError?.Invoke(new Error(ErrorType.InvalidToken, errorMessage: "Invalid refresh token"));

                return;
            }

            if (settings.OAuthClientId <= 0) {
                onError?.Invoke(new Error(ErrorType.InvalidToken, errorMessage: "Cannot refresh token without a valid OAuth2 client ID"));

                return;
            }

            var requestData = new WWWForm();
            requestData.AddField("client_id", settings.OAuthClientId);
            requestData.AddField("redirect_uri", RedirectUrlHelper.GetRedirectUrl(settings, redirectUri));
            requestData.AddField("grant_type", "refresh_token");
            requestData.AddField("refresh_token", refreshToken);

            const string url = BASE_URL + "/oauth2/token";

            WebRequestHelper.Instance.PostRequest<TokenResponse>(
                SdkType.Login,
                url,
                requestData,
                response =>
                {
                    // Memorize the token's origin so that we know in the future whether authentication
                    // via a device ID can be used in case of a fatal issue with the existing token.
                    var isBasedOnDeviceId = settings.XsollaToken.Exists && settings.XsollaToken.IsBasedOnDeviceId;
                    settings.XsollaToken.Create(response.access_token, response.refresh_token, response.expires_in, isBasedOnDeviceId);
                    onSuccess?.Invoke();
                },
                error => onError?.Invoke(error));
        }

        /// <summary>
        /// Refreshes the social token embedded in the current main JWT. Requires the
        /// main token to still be valid — the call uses it as Bearer authentication.
        /// </summary>
        /// <param name="provider">Name of the social network whose embedded token should be refreshed.</param>
        /// <param name="onSuccess">Called after the social token is refreshed and the new main JWT is stored.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        internal static void RefreshSocialToken(XsollaSettings settings, string provider, Action onSuccess, Action<Error> onError)
        {
            var authHeader = WebRequestHeader.AuthHeader(settings);

            if (authHeader == null) {
                var error = new Error(ErrorType.InvalidToken, errorMessage: "Cannot refresh social token without a valid main auth token");
                XDebug.LogError(settings, $"XsollaAuth.RefreshSocialToken: aborting (provider={provider}); {error}");
                onError?.Invoke(error);

                return;
            }

            XDebug.Log(settings, $"XsollaAuth.RefreshSocialToken: requesting new main token with refreshed social claims (provider={provider})");

            var existingRefreshToken = settings.XsollaToken.RefreshToken;
            var isBasedOnDeviceId = settings.XsollaToken.Exists && settings.XsollaToken.IsBasedOnDeviceId;

            var url = new UrlBuilder(BASE_URL + $"/social/{provider}/refresh_token")
                .AddProjectId(settings.LoginId)
                .Build();

            // Native parity: the Java SDK posts `{}` as the body. Sending no body
            // at all (UnityWebRequest's default when jsonObject is null) leaves
            // some Xsolla deployments rejecting the request for missing content.
            WebRequestHelper.Instance.PostRequest<TokenResponseSimple, EmptyBody>(
                SdkType.Login,
                url,
                new EmptyBody(),
                authHeader,
                response =>
                {
                    if (JwtUtils.TryDecodeExp(response.token, out var expirationEpochSeconds)) {
                        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        XDebug.Log(settings, $"XsollaAuth.RefreshSocialToken: succeeded (provider={provider}); new main token expiry epoch={expirationEpochSeconds} (relative={expirationEpochSeconds - nowEpoch}s)");
                        settings.XsollaToken.CreateWithEpochExpiration(response.token, existingRefreshToken, expirationEpochSeconds, isBasedOnDeviceId);
                    } else {
                        XDebug.LogWarning(settings, $"XsollaAuth.RefreshSocialToken: succeeded (provider={provider}) but new main token has no decodable exp claim; storing without expiry");
                        settings.XsollaToken.Create(response.token, existingRefreshToken, isBasedOnDeviceId);
                    }

                    XDebug.LogDebug(settings, $"XsollaAuth.RefreshSocialToken: new main token = {response.token}");
                    onSuccess?.Invoke();
                },
                e =>
                {
                    XDebug.LogError(settings, $"XsollaAuth.RefreshSocialToken: request failed (provider={provider}): {e}");
                    onError?.Invoke(e);
                },
                ErrorGroup.LoginErrors);
        }

        /// <summary>
        /// Refreshes the main token by spending the still-valid social token embedded
        /// in the current JWT. The new main token will not carry the original social
        /// claims — they are "decayed" away in the exchange.
        /// </summary>
        /// <param name="provider">Name of the social network whose embedded access token should be exchanged.</param>
        /// <param name="socialAccessToken">Social access token sourced from the current JWT.</param>
        /// <param name="onSuccess">Called after the new main token is stored.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        internal static void RefreshViaSocialClaimsDecay(XsollaSettings settings, string provider, string socialAccessToken, Action onSuccess, Action<Error> onError)
        {
            if (settings.OAuthClientId <= 0) {
                var error = new Error(ErrorType.InvalidToken, errorMessage: "Cannot refresh via social claims decay without a valid OAuth2 client ID");
                XDebug.LogError(settings, $"XsollaAuth.RefreshViaSocialClaimsDecay: aborting (provider={provider}); {error}");
                onError?.Invoke(error);

                return;
            }

            // Preserve the origin marker so that, if the new token later expires
            // without an OAuth2 refresh path, TryReauth can still fall back to
            // silent device-ID re-auth.
            var isBasedOnDeviceId = settings.XsollaToken.Exists && settings.XsollaToken.IsBasedOnDeviceId;

            XDebug.Log(settings, $"XsollaAuth.RefreshViaSocialClaimsDecay: exchanging still-valid social access token for a fresh OAuth2 token pair (provider={provider}, isBasedOnDeviceId={isBasedOnDeviceId}); social claims will not be carried by the new main token");
            XDebug.LogDebug(settings, $"XsollaAuth.RefreshViaSocialClaimsDecay: social access token being spent = {socialAccessToken}");

            // Native parity: the Java SDK pins this exchange's `redirect_uri` to
            // the `api/blank` sentinel rather than the consumer's configured
            // callback. The endpoint returns a `login_url` we parse the auth
            // code out of locally — no actual redirect is followed — so a stable
            // allowlisted URI avoids spurious rejections from OAuth2 clients
            // whose registered callback differs from the runtime CallbackUrl.
            ExchangeSocialTokenForOAuth2(settings, provider, socialAccessToken, accessTokenSecret: null, openId: null, isBasedOnDeviceId,
                onSuccess: () =>
                {
                    XDebug.Log(settings, $"XsollaAuth.RefreshViaSocialClaimsDecay: succeeded (provider={provider})");
                    onSuccess?.Invoke();
                },
                onError: e =>
                {
                    XDebug.LogError(settings, $"XsollaAuth.RefreshViaSocialClaimsDecay: failed (provider={provider}): {e}");
                    onError?.Invoke(e);
                },
                redirectUri: Constants.DEFAULT_REDIRECT_URL
            );
        }

        /// <summary>
        /// Exchanges the user authentication code to a valid JWT.
        /// </summary>
        /// <param name="code">Access code received from several other OAuth 2.0 requests (example: code from social network authentication).</param>
        /// <param name="onSuccess">Called after successful exchanging. Contains exchanged token.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        /// <param name="redirectUri">URI to redirect the user to after account confirmation, successful authentication, two-factor authentication configuration, or password reset confirmation.
        ///     Must be identical to the OAuth 2.0 redirect URIs specified in Publisher Account.
        ///     Required if there are several URIs.</param>
        /// <param name="sdkType">SDK type. Used for internal analytics.</param>
        public static void ExchangeCodeToToken(XsollaSettings settings, string code, Action onSuccess, Action<Error> onError, string redirectUri = null, SdkType sdkType = SdkType.Login)
        {
            ExchangeCodeToToken(settings, code, isBasedOnDeviceId: false, onSuccess, onError, redirectUri, sdkType);
        }

        internal static void ExchangeCodeToToken(XsollaSettings settings, string code, bool isBasedOnDeviceId, Action onSuccess, Action<Error> onError, string redirectUri = null, SdkType sdkType = SdkType.Login)
        {
            const string url = BASE_URL + "/oauth2/token";

            var requestData = new WWWForm();
            requestData.AddField("client_id", settings.OAuthClientId);
            requestData.AddField("redirect_uri", RedirectUrlHelper.GetRedirectUrl(settings, redirectUri));
            requestData.AddField("grant_type", "authorization_code");
            requestData.AddField("code", code);

            WebRequestHelper.Instance.PostRequest<TokenResponse>(
                sdkType,
                url,
                requestData,
                response =>
                {
                    settings.XsollaToken.Create(response.access_token, response.refresh_token, response.expires_in, isBasedOnDeviceId);
                    onSuccess?.Invoke();
                },
                error => onError?.Invoke(error));
        }

        /// <summary>
        /// Returns user details.
        /// </summary>
        /// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/user-account-and-attributes/user-account/).</remarks>
        /// <param name="onSuccess">Called after successful user details were successfully received.</param>
        /// <param name="onError">Called after the request resulted with an error.</param>
        public static void GetUserInfo(XsollaSettings settings, Action<UserInfo> onSuccess, Action<Error> onError)
        {
            WebRequestHelper.Instance.GetRequest(
                SdkType.Login,
                BASE_URL + "/users/me",
                WebRequestHeader.AuthHeader(settings),
                onSuccess,
                onError);
        }

        private static void ExchangeSocialTokenForOAuth2(XsollaSettings settings, string provider, string socialAccessToken, string accessTokenSecret, string openId, bool isBasedOnDeviceId, Action onSuccess, Action<Error> onError, string redirectUri = null, string state = null)
        {
            var url = new UrlBuilder(BASE_URL + $"/oauth2/social/{provider}/login_with_token")
                .AddClientId(settings.OAuthClientId)
                .AddResponseType(GetResponseType())
                .AddRedirectUri(RedirectUrlHelper.GetRedirectUrl(settings, redirectUri))
                .AddState(GetState(state))
                .AddScope(GetScope())
                .Build();

            var requestData = new AuthWithSocialNetworkAccessTokenRequest
            {
                access_token = socialAccessToken,
                access_token_secret = accessTokenSecret,
                openId = openId
            };

            WebRequestHelper.Instance.PostRequest<LoginLink, AuthWithSocialNetworkAccessTokenRequest>(
                SdkType.Login,
                url,
                requestData,
                link => ParseCodeFromUrlAndExchangeToToken(settings, link.login_url, isBasedOnDeviceId, onSuccess, onError),
                onError,
                ErrorGroup.LoginErrors);
        }

        private static void ParseCodeFromUrlAndExchangeToToken(XsollaSettings settings, string url, bool isBasedOnDeviceId, Action onSuccess, Action<Error> onError)
        {
            if (ParseUtils.TryGetValueFromUrl(url, ParseParameter.code, out var parsedCode))
                ExchangeCodeToToken(settings, parsedCode, isBasedOnDeviceId, onSuccess, onError);
            else
                onError?.Invoke(Error.UnknownError);
        }

        private static string GetState(string oauthState)
        {
            return !string.IsNullOrEmpty(oauthState) ? oauthState : "xsollatest";
        }

        private static string GetResponseType()
        {
            return "code";
        }

        private static string GetScope()
        {
            return "offline";
        }
    }
}
