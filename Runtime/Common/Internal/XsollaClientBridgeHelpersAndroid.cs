#if UNITY_ANDROID || UNITY_EDITOR
using System;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Scripting;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Common
{
    internal static class XsollaClientBridgeHelpersAndroid
    {
        private const string Tag = "XsollaClientBridgeHelpersAndroid";
        
        #region Java Calls 
        
        private const string BASE_CLASS_NAME = "com.xsolla.mobile.unity.XsollaStoreClientNativeAndroid";

        public readonly struct AndroidActivity
        {
            public readonly AndroidJavaObject activity;

            public AndroidActivity(AndroidJavaObject activity)
            {
                this.activity = activity;
            }
        }

        public static readonly Lazy<AndroidActivity> androidActivity = new Lazy<AndroidActivity>(() =>
        {
            var javaClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = javaClass.GetStatic<AndroidJavaObject>("currentActivity");
            return new AndroidActivity(activity);
        });

        private static readonly Lazy<AndroidJavaObject> _androidStore = new Lazy<AndroidJavaObject>(() =>
            new AndroidJavaObject(BASE_CLASS_NAME, androidActivity.Value.activity)
        );

        public static readonly Lazy<AndroidJavaClass> androidStoreClassLazy = new Lazy<AndroidJavaClass>(() => new AndroidJavaClass(BASE_CLASS_NAME));

        public static void JavaCall(string method, string json, XsollaUnityBridgeJsonCallback callback) =>
            JavaCall(_androidStore.Value, method, callback.onError, json, callback);
        
        public static void JavaCall(string method, Action<string> onError, params object[] args) =>
            JavaCall(_androidStore.Value, method, onError, args);

        public static void JavaCall(AndroidJavaObject obj, string method, Action<string> onError, params object[] args) {
            try {
                obj.Call(method, args);
            } catch (Exception e) {
                XsollaLogger.Error(Tag, $"JavaCall: {method} - failed with: {e}");
                onError?.Invoke($"{method} - {e}");
            }
        }

        public static A JavaCallStatic<A>(string methodName, params object[] args)
        {
            return androidStoreClassLazy.Value.CallStatic<A>(methodName, args);
        }
        
        public static void JavaCallStatic(string methodName, params object[] args)
        {
            androidStoreClassLazy.Value.CallStatic(methodName, args);
        }

        #endregion
        
        #region Callbacks

        public sealed class XsollaUnityBridgeJsonCallback : AndroidJavaProxy {
            private static readonly string CLASS_NAME = $"{BASE_CLASS_NAME}$IXsollaUnityBridgeJsonCallback";

            public readonly XsollaClientCallback callback;

            public XsollaUnityBridgeJsonCallback(XsollaClientCallback.CallbackDelegate callback) : base(CLASS_NAME) {
                this.callback = new XsollaClientCallback(callback);
            }

            [Preserve] public void onSuccess(string result) => callback.RunAndDequeueCallbacks(result, null);
            [Preserve] public void onError(string error) => callback.RunAndDequeueCallbacks(null, error);  
        }

        private static readonly Lazy<XsollaUnityBridgeJsonCallback> DummyLazy =
            new Lazy<XsollaUnityBridgeJsonCallback>(() => CreateCallback("Dummy", _ => { }, _ => { }));

        public static XsollaUnityBridgeJsonCallback Dummy => DummyLazy.Value;

        public static XsollaUnityBridgeJsonCallback CreateCallback(string logTag, Action<string> onSuccess, Action<string> onError)
        {
            return new XsollaUnityBridgeJsonCallback((result, error) => {
                
                XsollaLogger.Debug(Tag, $"{logTag} callback fire result={result} error={error}");
                
                RunOnStartThread.Run(() => {
                    if (result != null) {
                        XsollaLogger.Debug(Tag, $"{logTag} result={result}");
                        onSuccess(result);
                    } else {
                        ReportError(Tag, $"{logTag} failed={error}");
                        onError(error ?? string.Empty);
                    }
                });
            });
        }

        public static XsollaUnityBridgeJsonCallback CreateCommonListener(string name, [CanBeNull] XsollaClientCallback.CallbackDelegate callback = default)
        {
            return new XsollaUnityBridgeJsonCallback(callback ?? ((result, error) => {
                XsollaLogger.Debug(Tag, $"{name} common callback fire result={result} error={error}");
            }));
        }
        
        public static void AddCommonListenerCallback(
            XsollaUnityBridgeJsonCallback listener, 
            string name, Action<string> onSuccess, Action<string> onError, [CanBeNull] XsollaClientCallback.ListenerDelegate onValidate = default
        ) {
            listener.callback.AddListenerCallback(Tag, name, onSuccess, onError);
        }

        public static void ReportError(string tag, string str)
        {
            if (!str.Contains("Cancelled")) {
                XsollaLogger.Error(tag, str);
            } else {
                XsollaLogger.Warning(tag, str);
            }
        }

        #endregion
        
        
    }
}
#endif