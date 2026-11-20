namespace Xsolla.Core
{
	/// <summary>
	/// The OAuth 2.0 `state` parameter: a per-request CSRF nonce echoed back on the login callback.
	/// </summary>
	internal static class OAuthState
	{
		/// <summary>
		/// Used only when no secure random source exists.
		/// </summary>
		private const string FALLBACK_STATE = "xsollatest";

		/// <summary>
		/// <paramref name="callerState"/> when the integration supplied one, otherwise a fresh random value.
		/// </summary>
		public static string Resolve(string callerState)
		{
			if (!string.IsNullOrEmpty(callerState))
				return callerState;

			return CryptoRandom.Base64UrlUnpadded(32) ?? FALLBACK_STATE;
		}

		/// <summary>
		/// Whether <paramref name="callbackUrl"/> carries exactly <paramref name="expectedState"/>. A
		/// missing value fails as a different one does.
		/// </summary>
		/// <remarks>
		/// For a redirect that arrives through the browser, where the state is the only thing tying it
		/// to the sign-in that opened the page.
		/// </remarks>
		public static bool IsEchoed(string expectedState, string callbackUrl)
		{
			return !string.IsNullOrEmpty(expectedState)
				&& !string.IsNullOrEmpty(callbackUrl)
				&& ParseUtils.TryGetValueFromUrl(callbackUrl, ParseParameter.state, out var echoedState)
				&& echoedState == expectedState;
		}

		/// <summary>
		/// Warns when <paramref name="callbackUrl"/> carries a state other than <paramref name="expectedState"/>.
		/// </summary>
		/// <remarks>
		/// Only warns: MFA, the extra-fields form and the hand-off to a native app answer without a state,
		/// so a strict check would break them.
		/// </remarks>
		public static void LogMismatch(string expectedState, string callbackUrl)
		{
			if (string.IsNullOrEmpty(expectedState) || string.IsNullOrEmpty(callbackUrl))
				return;

			// No state echoed back: nothing to compare, not a mismatch.
			if (!ParseUtils.TryGetValueFromUrl(callbackUrl, ParseParameter.state, out var echoedState))
				return;

			if (echoedState != expectedState)
				XDebug.LogWarning("OAuth state on the login callback does not match the one sent for this request");
		}
	}
}
