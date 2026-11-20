using System.Text;
using System.Text.RegularExpressions;

namespace Xsolla.Core
{
	/// <summary>
	/// Masks secret values in text bound for the log: request URLs, form bodies, responses and tokens.
	/// </summary>
	/// <remarks>
	/// Off until a client configuration turns it on with <c>secretsObfuscated</c>; then it masks in every build.
	/// </remarks>
	internal static class LogRedaction
	{
		private const string MASK = "<redacted>";

		private const string ABSENT = "<absent>";

		private const int MIN_TAILED_LENGTH = 16;

		private const int TAIL_LENGTH = 12;

		private const ulong FNV_OFFSET_BASIS = 0xCBF29CE484222325UL;

		private const ulong FNV_PRIME = 0x100000001B3UL;

		private static volatile bool _isEnabled;

		/// <summary>
		/// Whether secrets are masked: set from each client configuration as it is applied, for the whole app.
		/// </summary>
		public static bool IsEnabled
		{
			get => _isEnabled;
			set => _isEnabled = value;
		}

		/// <summary>
		/// The value of a query or form parameter that is a secret: the PKCE verifier, the authorization
		/// code it redeems, the state that ties a redirect to its sign-in, and any token.
		/// </summary>
		/// <remarks>
		/// Matched as <c>name=value</c> pairs only, which is also how they appear in a <c>login_url</c>
		/// inside a JSON response, where <c>&amp;</c> may be escaped as <c>&</c>. A bare
		/// <c>"code"</c> JSON key is an error code, so it stays readable.
		/// </remarks>
		private static readonly Regex SecretParameterValue = new Regex(
			@"(?<=(^|[?&#]|\\u0026)(code_verifier|code|state|([a-z]+_)*token(_secret)?)=)[^&#\s""\\]+",
			RegexOptions.Multiline
		);

		/// <summary>
		/// The string value of a JSON key that names a token or its secret: <c>token</c>, <c>access_token</c>,
		/// <c>social_refresh_token</c>, <c>access_token_secret</c> and the like.
		/// </summary>
		private static readonly Regex SecretJsonValue = new Regex(
			@"(?<=""([a-z]+_)*token(_secret)?""\s*:\s*"")[^""]+"
		);

		/// <summary>
		/// UTF-8 that writes an unpaired surrogate as <c>?</c>, the way Java's <c>String.getBytes(UTF_8)</c> does,
		/// so that even a malformed value hashes as it does natively.
		/// </summary>
		private static readonly Encoding JavaUtf8 = Encoding.GetEncoding(
			"utf-8", new EncoderReplacementFallback("?"), new DecoderReplacementFallback()
		);

		/// <summary>
		/// The text with its secrets masked while masking is on, unchanged otherwise.
		/// </summary>
		public static string Redact(string text)
		{
			return _isEnabled ? MaskSecrets(text) : text;
		}

		/// <summary>
		/// The last characters of a token while masking is on, enough to tell two tokens apart without exposing
		/// either; the whole token otherwise.
		/// </summary>
		public static string Tail(string secret)
		{
			return _isEnabled ? MaskAllButTail(secret) : secret;
		}

		internal static string MaskSecrets(string text)
		{
			return string.IsNullOrEmpty(text)
				? text
				: SecretJsonValue.Replace(SecretParameterValue.Replace(text, MASK), MASK);
		}

		/// <remarks>
		/// The native Android SDK's <c>StringUtils.obfuscate</c> format, so that one token reads the same in this log
		/// and the native one. A value too short to spare its tail is replaced by a hash of it, which still tells two
		/// values apart.
		/// <para>
		/// Mirrors <c>Xsolla.SDK.Utils.LogObfuscation.Obfuscate</c> rather than calling it: this assembly also builds
		/// on its own, into the standalone DLL of <c>XsollaSDKUnityDll</c>, and compiles under
		/// <c>XSOLLA_SDK_DISABLE</c>, neither of which <c>Xsolla.SDK.Utils</c> is part of.
		/// </para>
		/// </remarks>
		internal static string MaskAllButTail(string secret)
		{
			if (string.IsNullOrEmpty(secret))
				return ABSENT;
			else if (secret.Length < MIN_TAILED_LENGTH)
				return "#" + ((uint)StableHash64(secret)).ToString("x8");
			else
				return "..." + secret.Substring(secret.Length - TAIL_LENGTH);
		}

		/// <summary>
		/// The native SDK's <c>HashUtils.computeStableHash64</c>: FNV-1a over the UTF-8 bytes, finalized with the
		/// MurmurHash3 64-bit mixer.
		/// </summary>
		private static ulong StableHash64(string value)
		{
			unchecked
			{
				var hash = FNV_OFFSET_BASIS;

				foreach (var b in JavaUtf8.GetBytes(value))
					hash = (hash ^ b) * FNV_PRIME;

				hash ^= hash >> 33;
				hash *= 0xFF51AFD7ED558CCDUL;
				hash ^= hash >> 33;
				hash *= 0xC4CEB9FE1A85EC53UL;
				hash ^= hash >> 33;

				return hash;
			}
		}
	}
}
