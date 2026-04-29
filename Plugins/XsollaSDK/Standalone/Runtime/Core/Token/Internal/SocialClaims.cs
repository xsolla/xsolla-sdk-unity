using System;

namespace Xsolla.Core
{
    internal sealed class SocialClaims
    {
        public string Provider { get; }

        public string AccessToken { get; }

        public string RefreshToken { get; }

        public bool HasRefreshToken => !string.IsNullOrEmpty(RefreshToken);

        /// <summary>
        /// Treats access tokens with no decodable `exp` as not-expired so that
        /// social claims decay (which requires a still-valid social token) can
        /// proceed when expiry information is unavailable.
        /// </summary>
        public bool IsAccessTokenExpired
        {
            get
            {
                if (!JwtUtils.TryDecodeExp(AccessToken, out var exp))
                    return false;

                return exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
        }

        public SocialClaims(string provider, string accessToken, string refreshToken)
        {
            Provider = provider;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
        }
    }
}
