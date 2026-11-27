#if UNITY_ANDROID || UNITY_EDITOR
using Xsolla.SDK.Common;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Login
{
    internal sealed class XsollaLoginClientImplAndroid : IXsollaLoginClient
    {
        private const string Tag = "XsollaLoginClientImplAndroid";

        public void Login(XsollaClientConfiguration configuration, LoginResultFunc onSuccess, ErrorFunc onError) =>
            LoginImpl(configuration, javaMethodName: "LoginWithWidget", logTag: "Login", onSuccess, onError);

        public void LoginSilently(XsollaClientConfiguration configuration, LoginResultFunc onSuccess, ErrorFunc onError) =>
            LoginImpl(configuration, javaMethodName: "LoginSilently", logTag: "LoginSilently", onSuccess, onError);

        public void LoginWithSocialAccount(XsollaClientConfiguration configuration, string provider, string accountToken, LoginResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, $"[{nameof(LoginWithSocialAccount)}]:\n" +
                $"\tConfiguration: {configuration}\n" +
                $"\tProvider: {provider}\n" +
                $"\tAccountToken: {LogObfuscation.ObfuscateIfEnabled(accountToken)}\n"
            );

            RunOnStartThread.Create();

            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "LoginWithSocialAccount",
                onError: error => onError?.Invoke(error),
                args: () => new object[] {
                    XsollaClientHelpers.ConfigurationToJson(configuration),
                    provider,
                    accountToken,
                    XsollaClientBridgeHelpersAndroid.CreateCallback( $"{nameof(LoginWithSocialAccount)}",
                        onSuccess: token => {
                            XsollaLogger.Debug(Tag, $"{nameof(LoginWithSocialAccount)} (token={LogObfuscation.ObfuscateJsonIfEnabled(token)})");
                            onSuccess?.Invoke(XsollaLoginClientHelpers.JsonToLoginTokenInfo(token));
                        },
                        onError: error => onError?.Invoke(error)
                    )
                }
            );
        }

        public void ClearToken(
            XsollaClientConfiguration configuration, ClearTokenResultFunc onSuccess, ErrorFunc onError
        )
        {
            XsollaLogger.Debug(Tag, $"{nameof(ClearToken)}: {configuration}");

            RunOnStartThread.Create();

            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "ClearToken",
                onError: error => onError?.Invoke(error),
                args: () => new object[] {
                    XsollaClientHelpers.ConfigurationToJson(configuration),
                    XsollaClientBridgeHelpersAndroid.CreateCallback( "ClearToken",
                        onSuccess: result => {
                            XsollaLogger.Debug(Tag, $"Cleared token (result={LogObfuscation.ObfuscateJsonIfEnabled(result)})");
                            onSuccess?.Invoke();
                        },
                        onError: error => onError?.Invoke(error)
                    )
                }
            );
        }

        public void RefreshToken(
            XsollaClientConfiguration configuration, XsollaLoginToken token, LoginResultFunc onSuccess, ErrorFunc onError
        ){
            XsollaLogger.Debug(Tag, $"RefreshToken: {configuration}");

            RunOnStartThread.Create();

            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "RefreshToken",
                onError: error => onError?.Invoke(error),
                args: () => new object[] {
                    XsollaClientHelpers.ConfigurationToJson(configuration),
                    XsollaClientHelpers.ToJson(token),
                    XsollaClientBridgeHelpersAndroid.CreateCallback( "RefreshToken",
                        onSuccess: result => {
                            XsollaLogger.Debug(Tag, $"Refreshed token (result={LogObfuscation.ObfuscateJsonIfEnabled(result)})");
                            onSuccess?.Invoke(XsollaLoginClientHelpers.JsonToLoginTokenInfo(result));
                        },
                        onError: error => onError?.Invoke(error)
                    )
                }
            );
        }

        private static void LoginImpl(
            XsollaClientConfiguration configuration, string javaMethodName, string logTag,
            LoginResultFunc onSuccess, ErrorFunc onError
        )
        {
            XsollaLogger.Debug(Tag, logTag);

            RunOnStartThread.Create();

            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: javaMethodName,
                json: () => XsollaClientHelpers.ConfigurationToJson(configuration),
                logTag: logTag,
                onSuccess: result => onSuccess?.Invoke(XsollaLoginClientHelpers.JsonToLoginTokenInfo(result)),
                onError: error => onError?.Invoke(error)
            );
        }

        
    }
}
#endif