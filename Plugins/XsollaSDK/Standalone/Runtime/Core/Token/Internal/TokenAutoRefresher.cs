using System;
using Xsolla.Auth;

namespace Xsolla.Core
{
	internal static class TokenAutoRefresher
	{
		public static void Check(XsollaSettings settings, Error error, Action<Error> onError, Action onSuccess)
		{
			if (error.ErrorType != ErrorType.InvalidToken && error.ErrorType != ErrorType.Unauthorized)
			{
				onError?.Invoke(error);
				return;
			}

			XDebug.Log(settings, $"TokenAutoRefresher.Check: server rejected token; "
				+ $"oauth2={settings.OAuthClientId > 0}, isBasedOnDeviceId={settings.XsollaToken.IsBasedOnDeviceId}, "
				+ $"hasRefreshToken={!string.IsNullOrEmpty(settings.XsollaToken.RefreshToken)}; "
				+ $"originating error: {error}");

			if (settings.OAuthClientId > 0)
				TryOAuth2RefreshWithChaining(settings, onSuccess, onError);
			else
				TryReauth(settings, onSuccess, onError);
		}

		// Refreshes social claims embedded in the current main JWT when the social
		// access token has expired but a social refresh token is still available.
		// No-ops (calls onSuccess) when no refresh is needed. Requires the main
		// auth token to be valid so it can be used as Bearer for the refresh call.
		internal static void CheckAndRefreshSocialIfNeeded(XsollaSettings settings, Action onSuccess, Action<Error> onError)
		{
			if (!JwtUtils.TryDecodeSocialClaims(settings.XsollaToken.AccessToken, out var claims))
			{
				XDebug.Log(settings, "TokenAutoRefresher.CheckAndRefreshSocialIfNeeded: no social claims found in token; nothing to refresh");
				onSuccess?.Invoke();
			}
			else if (!claims.IsAccessTokenExpired)
			{
				XDebug.Log(settings, $"TokenAutoRefresher.CheckAndRefreshSocialIfNeeded: social access token still valid (provider={claims.Provider}); nothing to refresh");
				onSuccess?.Invoke();
			}
			else if (!claims.HasRefreshToken)
			{
				XDebug.Log(settings, $"TokenAutoRefresher.CheckAndRefreshSocialIfNeeded: social access token expired (provider={claims.Provider}) and no social refresh token embedded; main token remains valid, proceeding without refreshed social claims");
				onSuccess?.Invoke();
			}
			else
			{
				XDebug.Log(settings, $"TokenAutoRefresher.CheckAndRefreshSocialIfNeeded: social access token expired (provider={claims.Provider}); requesting refresh");
				XDebug.LogDebug(settings, $"TokenAutoRefresher.CheckAndRefreshSocialIfNeeded: claims = {{provider={claims.Provider}, accessToken={claims.AccessToken}, refreshToken={claims.RefreshToken}}}");

				// Failure here is non-fatal: the main JWT is still valid, so calls
				// that don't depend on social claims continue to work. Calls that
				// do will fail at request time and route back through `Check()`,
				// where the refresh chain gets another attempt.
				XsollaAuth.RefreshSocialToken(settings, claims.Provider,
					onSuccess,
					onError: e => {
						XDebug.LogWarning(settings, $"TokenAutoRefresher.CheckAndRefreshSocialIfNeeded: social refresh failed (provider={claims.Provider}, {e}); main token is still valid, proceeding without refreshed social claims");
						onSuccess?.Invoke();
					});
			}
		}

		// Tries to obtain a fresh main token by spending the still-valid social
		// access token from the current JWT (claims decay). Falls back to a silent
		// device-ID re-auth if no eligible social claims are present.
		internal static void TryClaimsDecayOrReauth(XsollaSettings settings, Action onSuccess, Action<Error> onError)
		{
			if (settings.OAuthClientId <= 0)
			{
				XDebug.Log(settings, "TokenAutoRefresher.TryClaimsDecayOrReauth: claims decay unavailable (no OAuth2 client id); falling back to silent re-auth");

				TryReauth(settings, onSuccess, onError);
			}
			else if (!JwtUtils.TryDecodeSocialClaims(settings.XsollaToken.AccessToken, out var claims))
			{
				XDebug.Log(settings, "TokenAutoRefresher.TryClaimsDecayOrReauth: claims decay unavailable (no social claims in token); falling back to silent re-auth");

				TryReauth(settings, onSuccess, onError);
			}
			else if (claims.IsAccessTokenExpired)
			{
				XDebug.LogWarning(settings, $"TokenAutoRefresher.TryClaimsDecayOrReauth: claims decay unavailable (social access token also expired, provider={claims.Provider}); falling back to silent re-auth");

				TryReauth(settings, onSuccess, onError);
			}
			else
			{
				XDebug.Log(settings, $"TokenAutoRefresher.TryClaimsDecayOrReauth: spending still-valid social access token (provider={claims.Provider}) to mint a new main token");
				XDebug.LogDebug(settings, $"TokenAutoRefresher.TryClaimsDecayOrReauth: claims = {{provider={claims.Provider}, accessToken={claims.AccessToken}, refreshToken={claims.RefreshToken}}}");

				XsollaAuth.RefreshViaSocialClaimsDecay(settings, claims.Provider, claims.AccessToken,
					onSuccess,
					onError: e => {
						XDebug.LogWarning(settings, $"TokenAutoRefresher.TryClaimsDecayOrReauth: claims-decay refresh failed ({e}); falling back to silent re-auth");
						TryReauth(settings, onSuccess, onError);
					});
			}
		}

		private static void TryOAuth2RefreshWithChaining(XsollaSettings settings, Action onSuccess, Action<Error> onError)
		{
			XDebug.Log(settings, "TokenAutoRefresher.TryOAuth2RefreshWithChaining: invoking OAuth2 refresh");

			XsollaAuth.RefreshToken(settings,
				onSuccess: () => {
					XDebug.Log(settings, "TokenAutoRefresher.TryOAuth2RefreshWithChaining: OAuth2 refresh succeeded; checking embedded social claims on the new token");
					CheckAndRefreshSocialIfNeeded(settings, onSuccess, onError);
				},
				onError: e => {
					XDebug.LogWarning(settings, $"TokenAutoRefresher.TryOAuth2RefreshWithChaining: OAuth2 refresh failed ({e}); attempting claims-decay or silent re-auth fallback");
					TryClaimsDecayOrReauth(settings, onSuccess, onError);
				}
			);
		}

		// Last-resort silent re-auth: only viable when the original token came
		// from a device ID. Widget and social re-auth are skipped because they
		// require user interaction that cannot be triggered from a refresh path.
		private static void TryReauth(XsollaSettings settings, Action onSuccess, Action<Error> onError)
		{
			if (settings.XsollaToken.IsBasedOnDeviceId)
			{
				XDebug.Log(settings, "TokenAutoRefresher.TryReauth: re-authenticating silently via device ID");
				XsollaAuth.AuthViaDeviceID(settings, onSuccess, onError);
				return;
			}

			var error = new Error(ErrorType.InvalidToken,
				errorMessage: "Failed to refresh the existing token (token's origin is " +
					"unknown, cannot use authentication by device ID)"
			);

			XDebug.LogError(settings, $"TokenAutoRefresher.TryReauth: no silent re-auth path available; surfacing error: {error}");

			onError?.Invoke(error);
		}
	}
}
