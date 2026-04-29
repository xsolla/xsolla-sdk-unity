using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Xsolla.Core
{
    internal static class JwtUtils
    {
        // Internal provider identifiers as they appear in JWT `provider` claims.
        // Not in the public SocialProvider enum because they are Xsolla-internal
        // provider names, not end-user-facing social networks.
        private const string ProviderBabka = "babka";
        private const string ProviderXsolla = "xsolla";

        public static string DecodePayloadJson(string jwt)
        {
            if (!TryDecodePayload(jwt, out var payload))
                return null;

            return payload.ToString();
        }

        public static bool TryDecodeExp(string jwt, out long epochSeconds)
        {
            epochSeconds = 0;

            if (!TryDecodePayload(jwt, out var payload))
                return false;

            var expToken = payload["exp"];
            if (expToken == null || expToken.Type == JTokenType.Null)
                return false;

            var exp = expToken.Value<long?>();
            if (exp == null || exp.Value <= 0)
                return false;

            epochSeconds = exp.Value;

            return true;
        }

        public static bool TryDecodeSocialClaims(string jwt, out SocialClaims claims)
        {
            claims = null;

            if (!TryDecodePayload(jwt, out var payload))
                return false;

            XDebug.LogDebug($"JwtUtils.TryDecodeSocialClaims: payload = {payload}");

            var type = payload.Value<string>("type");
            if (!string.Equals(type, "social", StringComparison.OrdinalIgnoreCase))
                return false;

            var provider = payload.Value<string>("provider");
            if (!IsSupportedSocialProvider(provider))
                return false;

            var accessToken = payload.Value<string>("social_access_token");
            if (string.IsNullOrEmpty(accessToken))
                return false;

            var refreshToken = payload.Value<string>("social_refresh_token");

            // Normalize provider to lowercase so it can be safely interpolated
            // into URL paths that the API treats as case-sensitive.
            claims = new SocialClaims(
                provider.ToLowerInvariant(), accessToken, refreshToken
            );

            return true;
        }

        /// <summary>
        /// Only `babka` and `xsolla` carry social claims that the SDK can act on.
        /// Other providers are recognized as identifiers elsewhere but their
        /// JWT-embedded social tokens are ignored.
        /// </summary>
        private static bool IsSupportedSocialProvider(string provider)
        {
            return string.Equals(provider, ProviderBabka, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(provider, ProviderXsolla, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryDecodePayload(string jwt, out JObject payload)
        {
            payload = null;

            if (string.IsNullOrEmpty(jwt))
                return false;

            var parts = jwt.Split('.');
            if (parts.Length != 3 || string.IsNullOrEmpty(parts[1]))
                return false;

            try {
                var bytes = Convert.FromBase64String(NormalizeBase64Url(parts[1]));
                var json = Encoding.UTF8.GetString(bytes);
                payload = JObject.Parse(json);
                return true;
            } catch (Exception ex) {
                // Surface malformed JWTs at warning level: callers treat this as
                // "no claims/exp available" and proceed with safe fallbacks, but
                // silent failures here would mask real protocol/encoding bugs.
                XDebug.LogWarning($"JwtUtils: failed to decode JWT payload ({ex.GetType().Name}: {ex.Message})");
                XDebug.LogDebug($"JwtUtils: offending JWT was: {jwt}");
                return false;
            }
        }

        private static string NormalizeBase64Url(string base64Url)
        {
            var s = base64Url.Replace('-', '+').Replace('_', '/');
            return (s.Length % 4) switch
            {
                2 => s + "==",
                3 => s + "=",
                1 => s // Malformed — Convert.FromBase64String will throw; caught above
                ,
                _ => s
            };
        }
    }
}
