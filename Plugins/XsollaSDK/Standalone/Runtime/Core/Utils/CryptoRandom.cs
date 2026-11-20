using System;
using System.Security.Cryptography;

namespace Xsolla.Core
{
	/// <summary>
	/// Cryptographically secure random values in the base64url alphabet, for OAuth 2.0 values that
	/// have to be unguessable: the `state` CSRF nonce and the PKCE `code_verifier`.
	/// </summary>
	internal static class CryptoRandom
	{
		/// <summary>
		/// <paramref name="byteCount"/> random bytes as unpadded base64url, or `null` without a secure source.
		/// </summary>
		public static string Base64UrlUnpadded(int byteCount)
		{
			try
			{
				var randomBytes = new byte[byteCount];
				using (var rng = RandomNumberGenerator.Create())
				{
					rng.GetBytes(randomBytes);
				}

				return ToBase64UrlUnpadded(randomBytes);
			}
			catch (Exception e)
			{
				XDebug.LogWarning($"CryptoRandom: no secure random source available. {e.Message}");
				return null;
			}
		}

		/// <summary>base64url, unpadded, per RFC 4648 section 5 with `=` stripped.</summary>
		public static string ToBase64UrlUnpadded(byte[] bytes)
		{
			return Convert.ToBase64String(bytes)
				.Replace("+", "-")
				.Replace("/", "_")
				.Replace("=", "");
		}
	}
}
