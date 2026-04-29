using System;
using UnityEngine;

namespace Xsolla.Core
{
	internal class XsollaToken
	{
		private const string SaveKey = "XsollaSuperToken";

		/// <summary>
		/// Access token. Required for most API requests.
		/// </summary>
		public string AccessToken => Instance?.accessToken;

		/// <summary>
		/// Refresh token. Required to get a new access token.
		///	</summary>
		public string RefreshToken => Instance?.refreshToken;

		/// <summary>
		/// Access token expiration time. Seconds since the Unix epoch.
		///	</summary>
		public long ExpirationTime => Instance?.expirationTime ?? 0;

	    /// <summary>
	    /// Returns true, if the token has expired (<see cref="ExpirationTime"/> > 0).
	    /// </summary>
	    public bool IsExpired => !Exists ||
	        (ExpirationTime > 0 &&
	        DateTimeOffset.FromUnixTimeSeconds(ExpirationTime) <= DateTimeOffset.Now);

		public bool Exists => Instance != null;

	    /// <summary>
	    /// Returns true, if the token has been produced from a unique device ID.
	    /// </summary>
	    public bool IsBasedOnDeviceId => Instance?.isBasedOnDeviceId ?? false;

		private TokenData Instance { get; set; }

		/// <summary>
		/// Used for log level and store project ID only.
		/// </summary>
		private readonly XsollaSettings Settings;

		public XsollaToken(XsollaSettings settings)
		{
        	Settings = settings;
		}

		public void Create(string accessToken, bool isBasedOnDeviceId)
		{
			Instance = new TokenData {
				accessToken = accessToken,
				isBasedOnDeviceId = isBasedOnDeviceId
			};

			XDebug.Log(Settings, $"XsollaToken created (access only); IsBasedOnDeviceId: {Instance.isBasedOnDeviceId}");
			XDebug.LogDebug(Settings, $"XsollaToken access token: {Instance.accessToken}");

			SaveInstance();
		}

		public void Create(string accessToken, string refreshToken, bool isBasedOnDeviceId)
		{
			Instance = new TokenData {
				accessToken = accessToken,
				refreshToken = refreshToken,
				isBasedOnDeviceId = isBasedOnDeviceId
			};

			XDebug.Log(Settings, $"XsollaToken created (access and refresh); IsBasedOnDeviceId: {Instance.isBasedOnDeviceId}");
			XDebug.LogDebug(Settings, $"XsollaToken access token: {accessToken}\nRefresh token: {refreshToken}");

			SaveInstance();
		}

		public void Create(string accessToken, string refreshToken, int expiresIn, bool isBasedOnDeviceId)
		{
			Instance = new TokenData {
				accessToken = accessToken,
				refreshToken = refreshToken,
				expirationTime = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeSeconds(),
				isBasedOnDeviceId = isBasedOnDeviceId
			};

			XDebug.Log(Settings, "XsollaToken created (access and refresh and expiration time)"
				+ $"\nExpiration time: {DateTimeOffset.FromUnixTimeSeconds(ExpirationTime).ToLocalTime()}"
				+ $"\nIsBasedOnDeviceId: {Instance.isBasedOnDeviceId}"
			);
			XDebug.LogDebug(Settings, $"XsollaToken access token: {accessToken}\nRefresh token: {refreshToken}");

			SaveInstance();
		}

		// Distinct from `Create(..., int expiresIn, bool)` so callers cannot
		// accidentally pass an absolute epoch where a relative-from-now is
		// expected (or vice versa).
		public void CreateWithEpochExpiration(string accessToken, string refreshToken, long expirationEpochSeconds, bool isBasedOnDeviceId)
		{
			Instance = new TokenData {
				accessToken = accessToken,
				refreshToken = refreshToken,
				expirationTime = expirationEpochSeconds,
				isBasedOnDeviceId = isBasedOnDeviceId
			};

			XDebug.Log(Settings, "XsollaToken created (access and refresh and epoch expiration time)"
				+ $"\nExpiration time: {DateTimeOffset.FromUnixTimeSeconds(ExpirationTime).ToLocalTime()}"
				+ $"\nIsBasedOnDeviceId: {Instance.isBasedOnDeviceId}"
			);
			XDebug.LogDebug(Settings, $"XsollaToken access token: {accessToken}\nRefresh token: {refreshToken}");

			SaveInstance();
		}

		private string GetSaveKey()
		{
			return $"{SaveKey}-{Settings.StoreProjectId}";
		}
		
		private void SaveInstance()
		{
			var key = GetSaveKey();
			SaveInstance(key);
		}

		private void SaveInstance(string key)
		{
			if (Instance == null)
				return;

			var json = ParseUtils.ToJson(Instance);
			PlayerPrefs.SetString(key, json);
		}

		public bool TryLoadInstance()
		{
			var key = GetSaveKey();

			if (TryLoadInstance(SaveKey)) // Migration: load from legacy key, re-save under new key, then drop the old one
			{
				XDebug.Log(Settings, "XsollaToken migrated from legacy PlayerPrefs key");
				SaveInstance();
				PlayerPrefs.DeleteKey(SaveKey);
				return true;
			}

			if (TryLoadInstance(key))
				return true;

			XDebug.Log(Settings, "XsollaToken not found in PlayerPrefs");

			return false;
		}

		private bool TryLoadInstance(string key)
		{
			if (!PlayerPrefs.HasKey(key))
				return false;

			var json = PlayerPrefs.GetString(key);
			var data = ParseUtils.FromJson<TokenData>(json);

			if (data == null || string.IsNullOrEmpty(data.accessToken))
			{
				XDebug.Log(Settings,"XsollaToken not found in PlayerPrefs");
				return false;
			}

			Instance = data;
			XDebug.LogDebug(Settings, $"XsollaToken loaded; decoded payload: {JwtUtils.DecodePayloadJson(Instance.accessToken) ?? "(not decodable)"}");

			if (string.IsNullOrEmpty(RefreshToken))
			{
				XDebug.Log(Settings, $"XsollaToken loaded (access only); IsBasedOnDeviceId: {Instance.isBasedOnDeviceId}");
				XDebug.LogDebug(Settings, $"XsollaToken access token: {AccessToken}");
			}
			else if (ExpirationTime <= 0)
			{
				XDebug.Log(Settings, $"XsollaToken loaded (access and refresh); IsBasedOnDeviceId: {Instance.isBasedOnDeviceId}");
				XDebug.LogDebug(Settings, $"XsollaToken access token: {AccessToken}\nRefresh token: {RefreshToken}");
			}
			else
			{
				XDebug.Log(Settings, "XsollaToken loaded (access and refresh and expiration time)"
					+ $"\nExpiration time: {DateTimeOffset.FromUnixTimeSeconds(ExpirationTime).ToLocalTime()}"
					+ $"\nIsBasedOnDeviceId: {Instance.isBasedOnDeviceId}"
				);
				XDebug.LogDebug(Settings, $"XsollaToken access token: {AccessToken}\nRefresh token: {RefreshToken}");
			}

			return true;
		}

		public void DeleteSavedInstance()
		{
			var key = GetSaveKey();
			DeleteSavedInstance(key);
		}
		
		private void DeleteSavedInstance(string key)
		{
			Instance = null;
			PlayerPrefs.DeleteKey(key);
		}
	}
}
