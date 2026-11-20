using System.Text;
using System.Text.RegularExpressions;

namespace Xsolla.SDK.Utils
{
    /// <summary>
    /// Obfuscates secrets in text bound for the log, in the format of the native Android SDK's
    /// <c>StringUtils.obfuscate</c>, so that one value reads the same in the Unity log and the native one.
    /// </summary>
    /// <remarks>
    /// Off until a client configuration turns it on with <c>secretsObfuscated</c>; then it obfuscates in every build,
    /// as the native Android SDK's <c>LogSettings.withSecretsObfuscated</c> does.
    /// </remarks>
    internal static class LogObfuscation
    {
        private const string Absent = "<absent>";

        private const string Ellipsis = "...";

        private const int MinCrunchableLength = 16;

        private const int NumCrunchTailChars = 12;

        private const ulong FnvOffsetBasis = 0xCBF29CE484222325UL;

        private const ulong FnvPrime = 0x100000001B3UL;

        private static volatile bool _isEnabled;

        /// <summary>
        /// Whether secrets are obfuscated: set from each client configuration as it is applied, for the whole app.
        /// </summary>
        public static bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        /// <summary>
        /// UTF-8 that writes an unpaired surrogate as <c>?</c>, the way Java's <c>String.getBytes(UTF_8)</c> does,
        /// so that even a malformed value hashes as it does natively.
        /// </summary>
        private static readonly Encoding JavaUtf8 = Encoding.GetEncoding(
            "utf-8", new EncoderReplacementFallback("?"), new DecoderReplacementFallback()
        );

        /// <summary>
        /// The string value of a JSON key that names a token, a secret or a receipt: <c>accessToken</c>,
        /// <c>refresh_token</c>, <c>token</c>, <c>paymentToken</c>, <c>access_token_secret</c>, <c>receipt</c> and the like.
        /// </summary>
        private static readonly Regex SecretJsonValue = new Regex(
            @"(?<=""[A-Za-z_]*(?:[Tt]oken|[Ss]ecret|[Rr]eceipt)""\s*:\s*"")(?:[^""\\]|\\.)*(?="")"
        );

        /// <summary>
        /// The secret obfuscated while obfuscation is on, unchanged otherwise.
        /// </summary>
        public static string ObfuscateIfEnabled(string secret)
        {
            return _isEnabled ? Obfuscate(secret) : secret;
        }

        /// <summary>
        /// The JSON with its token, secret and receipt values obfuscated while obfuscation is on, unchanged otherwise.
        /// </summary>
        public static string ObfuscateJsonIfEnabled(string json)
        {
            return _isEnabled ? ObfuscateJson(json) : json;
        }

        /// <summary>
        /// The last characters of a secret, enough to tell two secrets apart without exposing either.
        /// </summary>
        /// <remarks>
        /// A value too short to spare its tail is replaced by a hash of it instead, which still tells two values apart.
        /// </remarks>
        internal static string Obfuscate(string secret)
        {
            if (string.IsNullOrEmpty(secret))
                return Absent;
            else if (secret.Length < MinCrunchableLength)
                return "#" + ((uint)StableHash64(secret)).ToString("x8");
            else
                return Ellipsis + secret.Substring(secret.Length - NumCrunchTailChars);
        }

        internal static string ObfuscateJson(string json)
        {
            return string.IsNullOrEmpty(json)
                ? json
                : SecretJsonValue.Replace(json, match => Obfuscate(match.Value));
        }

        /// <summary>
        /// The native SDK's <c>HashUtils.computeStableHash64</c>: FNV-1a over the UTF-8 bytes, finalized with the
        /// MurmurHash3 64-bit mixer.
        /// </summary>
        private static ulong StableHash64(string value)
        {
            unchecked
            {
                var hash = FnvOffsetBasis;

                foreach (var b in JavaUtf8.GetBytes(value))
                    hash = (hash ^ b) * FnvPrime;

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
