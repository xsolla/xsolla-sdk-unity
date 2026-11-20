using System;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Common
{
    /// <summary>
    /// Specifies the log levels for Xsolla SDK.
    /// </summary>
    public enum XsollaLogLevel
    {
        /// <summary>Debug level logging.</summary>
        Debug,
        /// <summary>Warning level logging.</summary>
        Warning,
        /// <summary>Error level logging.</summary>
        Error,
        /// <summary>No logging.</summary>
        None
    }

    /// <summary>
    /// Provides logging functionality for Xsolla SDK.
    /// </summary>
    public static class XsollaLogger
    {
        private const string Tag = "Xsolla SDK";
        private static XsollaLogLevel _level;
        private static Action<XsollaLogLevel, string> _onLogCallback;

        /// <summary>
        /// Logs a message with the specified log level, tag, and message.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="tag">The tag for the log message.</param>
        /// <param name="message">The log message.</param>
        private static void Log(XsollaLogLevel level, string tag, string message)
        {
            if (_level > level)
                return;

            switch (level)
            {
                case XsollaLogLevel.Debug:
                    UnityEngine.Debug.Log($"[{Tag}][{tag}]: {message}");
                    break;
                case XsollaLogLevel.Warning:
                    UnityEngine.Debug.LogWarning($"[{Tag}][{tag}]: {message}");
                    break;
                case XsollaLogLevel.Error:
                    UnityEngine.Debug.LogError($"[{Tag}][{tag}]: {message}");
                    break;
            }

            _onLogCallback?.Invoke(level, $"[{Tag}][{tag}]: {message}");
        }

        /// <summary>
        /// Sets the log level for the logger.
        /// </summary>
        /// <param name="level">The log level to set.</param>
        public static void SetLogLevel(XsollaLogLevel level) => _level = level;

        /// <summary>
        /// Applies a client configuration's log settings, its level and whether secrets are obfuscated, to the whole app.
        /// </summary>
        internal static void Apply(XsollaClientConfiguration configuration)
        {
            SetLogLevel(configuration.logLevel);
            LogObfuscation.IsEnabled = configuration.secretsObfuscated;
        }

        /// <summary>
        /// Sets the callback to be invoked on log events.
        /// </summary>
        /// <param name="callback">The callback to invoke with log level and message.</param>
        public static void SetOnLogCallback(Action<XsollaLogLevel, string> callback) => _onLogCallback = callback;

        /// <summary>
        /// Gets the current log level.
        /// </summary>
        /// <returns>The current <see cref="XsollaLogLevel"/>.</returns>
        public static XsollaLogLevel GetLogLevel() => _level;

        /// <summary>
        /// Gets the current log callback.
        /// </summary>
        /// <returns>The current log callback.</returns>
        public static Action<XsollaLogLevel, string> GetOnLogCallback() => _onLogCallback;

        /// <summary>
        /// Logs a debug message.
        /// </summary>
        /// <param name="tag">The tag for the log message.</param>
        /// <param name="message">The debug message.</param>
        public static void Debug(string tag, string message) => Log(XsollaLogLevel.Debug, tag, message);

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        /// <param name="tag">The tag for the log message.</param>
        /// <param name="message">The warning message.</param>
        public static void Warning(string tag, string message) => Log(XsollaLogLevel.Warning, tag, message);

        /// <summary>
        /// Logs an error message.
        /// </summary>
        /// <param name="tag">The tag for the log message.</param>
        /// <param name="message">The error message.</param>
        public static void Error(string tag, string message) => Log(XsollaLogLevel.Error, tag, message);
    }
}