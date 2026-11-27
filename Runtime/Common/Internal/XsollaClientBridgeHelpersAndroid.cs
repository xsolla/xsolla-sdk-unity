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

        /// <summary>
        /// Calls <paramref name="method"/> with the JSON <paramref name="json"/> builds and a <see cref="CreateCallback"/>
        /// callback, and answers anything either throws through that callback's error path, as the bridge's own error.
        /// <para/>
        /// Both are built inside the <c>try</c> of <see cref="JavaCall(string, Action{string}, Func{object[]})"/>.
        /// </summary>
        public static void JavaCall(
            string method, Func<string> json, string logTag, Action<string> onSuccess, Action<string> onError
        ) {
            var callback = CreateCallbackDelegate(logTag, onSuccess, onError);
            JavaCall(
                method,
                error => callback(null, error),
                () => new object[] { json(), new XsollaUnityBridgeJsonCallback(callback) }
            );
        }

        /// <summary>
        /// Calls <paramref name="method"/> on the bridge with the arguments <paramref name="args"/> builds, and reports
        /// anything either throws to <paramref name="onError"/>.
        /// <para/>
        /// The bridge is created inside the <c>try</c> too: a missing or stripped bridge class fails there, and the failed
        /// <see cref="Lazy{T}"/> rethrows that failure on every later call, so each call reports it instead of throwing at
        /// its caller. The arguments are built inside it for the same reason: a callback among them is an
        /// <see cref="AndroidJavaProxy"/> of a bridge interface, which a stripped bridge loses as well. They are built before
        /// the bridge is created, so an argument builder's side effects happen whether or not the bridge exists.
        /// </summary>
        public static void JavaCall(string method, [CanBeNull] Action<string> onError, Func<object[]> args) {
            try {
                var builtArgs = args();
                _androidStore.Value.Call(method, builtArgs);
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

        public static XsollaUnityBridgeJsonCallback CreateCallback(string logTag, Action<string> onSuccess, Action<string> onError) =>
            new XsollaUnityBridgeJsonCallback(CreateCallbackDelegate(logTag, onSuccess, onError));

        /// <summary>
        /// Creates the result handling of a <see cref="CreateCallback"/> callback, which answers on the start thread.
        /// </summary>
        private static XsollaClientCallback.CallbackDelegate CreateCallbackDelegate(
            string logTag, Action<string> onSuccess, Action<string> onError
        ) {
            return (result, error) => {
                
                XsollaLogger.Debug(Tag, $"{logTag} callback fire result={LogObfuscation.ObfuscateJsonIfEnabled(result)} error={error}");

                RunOnStartThread.Run(() => {
                    if (result != null) {
                        XsollaLogger.Debug(Tag, $"{logTag} result={LogObfuscation.ObfuscateJsonIfEnabled(result)}");
                        onSuccess(result);
                    } else {
                        ReportError(Tag, $"{logTag} failed={error}");
                        onError(error ?? string.Empty);
                    }
                });
            };
        }

        public static XsollaUnityBridgeJsonCallback CreateCommonListener(string name, [CanBeNull] XsollaClientCallback.CallbackDelegate callback = default)
        {
            return new XsollaUnityBridgeJsonCallback(callback ?? ((result, error) => {
                XsollaLogger.Debug(Tag, $"{name} common callback fire result={LogObfuscation.ObfuscateJsonIfEnabled(result)} error={error}");
            }));
        }
        
        /// <summary>
        /// Queues a callback that answers the next result of <paramref name="listener"/>.
        /// </summary>
        public static void AddCommonListenerCallback(
            XsollaUnityBridgeJsonCallback listener,
            string name, Action<string> onSuccess, Action<string> onError
        ) {
            listener.callback.AddListenerCallback(Tag, name, onSuccess, onError);
        }

        /// <summary>
        /// Creates a callback that answers one result of a listener, for a caller that routes the results itself.
        /// </summary>
        public static XsollaClientCallback.ListenerDelegate CreateCommonListenerCallback(
            string name, Action<string> onSuccess, Action<string> onError
        ) => XsollaClientCallbackExt.CreateListenerCallback(Tag, name, onSuccess, onError);

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