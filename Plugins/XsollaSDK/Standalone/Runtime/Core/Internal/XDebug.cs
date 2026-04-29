using System;
using UnityEngine;

namespace Xsolla.Core
{
	internal static class XDebug
	{
		private const string TAG = "[Xsolla SDK]";
		private static Action<LogLevel, string> _onLogCallback;
		private static LogLevel _logLevel = LogLevel.Errors;
		
		public static void SetLogLevel(LogLevel logLevel) => _logLevel = logLevel;
		public static void SetOnLogCallback(Action<LogLevel, string> callback) => _onLogCallback = callback;

		public static void Log(XsollaSettings settings, object message, bool ignoreLogLevel = false)
			=> Log(TAG, message, ignoreLogLevel, settings);
		
		public static void Log(object message, bool ignoreLogLevel = false)
			=> Log(TAG, message, ignoreLogLevel);

		public static void Log(string tag_, object message, bool ignoreLogLevel = false, XsollaSettings settings = null)
		{
			bool allow = (settings?.LogLevel ?? _logLevel) <= LogLevel.InfoWarningsErrors || ignoreLogLevel;
			if (allow)
			{
				var tag = settings?.LogTag ?? tag_;
				Debug.Log($"{tag} {message}");
				_onLogCallback?.Invoke(LogLevel.InfoWarningsErrors, $"{tag} {message}");
			}
		}

		public static void LogWarning(XsollaSettings settings, object message, bool ignoreLogLevel = false)
			=> LogWarning(TAG, message, ignoreLogLevel, settings);
		
		public static void LogWarning(object message, bool ignoreLogLevel = false)
			=> LogWarning(TAG, message, ignoreLogLevel, settings: null);

		public static void LogWarning(string tag_, object message, bool ignoreLogLevel = false, XsollaSettings settings = null)
		{
			bool allow = (settings?.LogLevel ?? _logLevel) <= LogLevel.WarningsErrors || ignoreLogLevel;
			if (allow)
			{
				var tag = settings?.LogTag ?? tag_;
				Debug.LogWarning($"{tag} {message}");
				_onLogCallback?.Invoke(LogLevel.WarningsErrors, $"{tag} {message}");
			}
		}
		
		public static void LogError(XsollaSettings settings, object message, bool ignoreLogLevel = false)
			=> LogError(TAG, message, ignoreLogLevel, settings);

		public static void LogError(object message, bool ignoreLogLevel = false)
			=> LogError(TAG, message, ignoreLogLevel);

		public static void LogError(string tag_, object message, bool ignoreLogLevel = false, XsollaSettings settings = null)
		{
			bool allow = (settings?.LogLevel ?? _logLevel) <= LogLevel.Errors || ignoreLogLevel;
			if (allow)
			{
				var tag = settings?.LogTag ?? tag_;
				Debug.LogError($"{tag} {message}");
				_onLogCallback?.Invoke(LogLevel.Errors, $"[ERROR]{tag} {message}");
			}
		}

		// Verbose, dev-only logging. Use for messages that may contain
		// sensitive information (raw tokens, decoded JWT payloads, request
		// bodies, etc.). Calls — including their argument evaluation — are
		// stripped at compile time when neither UNITY_EDITOR nor
		// DEVELOPMENT_BUILD is defined, so they cannot appear in production
		// player builds of consuming apps.
		[System.Diagnostics.Conditional("UNITY_EDITOR")]
		[System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
		public static void LogDebug(XsollaSettings settings, object message)
			=> Log(TAG, message, ignoreLogLevel: false, settings);

		[System.Diagnostics.Conditional("UNITY_EDITOR")]
		[System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
		public static void LogDebug(object message)
			=> Log(TAG, message, ignoreLogLevel: false);
	}
}