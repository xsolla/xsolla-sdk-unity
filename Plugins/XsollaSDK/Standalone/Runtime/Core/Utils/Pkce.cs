using System;
using System.Security.Cryptography;
using System.Text;

namespace Xsolla.Core
{
	/// <summary>
	/// PKCE (RFC 7636) parameters for a single authorization-code exchange.
	/// </summary>
	/// <remarks>
	/// An authorization code that leaks can't be redeemed without the verifier, which only the token
	/// exchange carries. Sign-in carries on without PKCE when no pair can be derived.
	/// </remarks>
	internal class Pkce
	{
		/// <summary>`code_verifier`: 32 random bytes, base64url, unpadded (43 characters).</summary>
		public string Verifier { get; }

		/// <summary>`code_challenge`: base64url(SHA-256(verifier)), unpadded. Always paired with `code_challenge_method=S256`.</summary>
		public string Challenge { get; }

		private Pkce(string verifier, string challenge)
		{
			Verifier = verifier;
			Challenge = challenge;
		}

		/// <summary>
		/// A fresh pair, or `null` if no secure random source or SHA-256 is available.
		/// </summary>
		public static Pkce Generate()
		{
			var verifier = CryptoRandom.Base64UrlUnpadded(32);
			if (string.IsNullOrEmpty(verifier))
			{
				XDebug.LogWarning("PKCE: no random bytes available, continuing without PKCE");
				return null;
			}
			else
			{
				return FromVerifier(verifier);
			}
		}

		/// <summary>
		/// The pair for a given verifier, or `null` if no SHA-256 is available.
		/// </summary>
		public static Pkce FromVerifier(string verifier)
		{
			try
			{
				// RFC 7636 hashes the verifier's ASCII characters, not the random bytes behind them.
				using (var sha256 = SHA256.Create())
				{
					var digest = sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier));
					return new Pkce(verifier, CryptoRandom.ToBase64UrlUnpadded(digest));
				}
			}
			catch (Exception e)
			{
				XDebug.LogWarning($"PKCE: could not derive a code challenge, continuing without PKCE. {e.Message}");
				return null;
			}
		}
	}
}
