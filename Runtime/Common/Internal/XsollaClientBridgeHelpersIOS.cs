#if UNITY_IOS || UNITY_EDITOR
using System;
using System.Collections.Generic;
using AOT;
using JetBrains.Annotations;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Common
{
    internal static class XsollaClientBridgeHelpersIOS
    {
        private const string Tag = "XsollaClientBridgeHelpersIOS";
        
        #region Callbacks
        
        private static Int64 _nextCallbackId;
        private static readonly Dictionary<Int64, XsollaClientCallback> _callbacks = new Dictionary<Int64, XsollaClientCallback>();
        private static readonly Dictionary<Int64, XsollaClientCallback> _listeners = new Dictionary<Int64, XsollaClientCallback>();
        
        public delegate void XsollaUnityBridgeJsonCallbackDelegate(Int64 callbackData, string jsonResult, string error);
        [MonoPInvokeCallback(typeof(XsollaUnityBridgeJsonCallbackDelegate))]
        public static void OnXsollaUnityBridgeJsonCallback(Int64 callbackData, string jsonResult, string error)
        {
            lock (_callbacks) {
                if (_callbacks.TryGetValue(callbackData, out var callback)) {
                    callback.onResult(jsonResult, error);
                    _callbacks.Remove(callbackData);
                }
            }
            
            lock (_listeners) {
                if (_listeners.TryGetValue(callbackData, out var callback))
                    callback.onResult(jsonResult, error);
            }
        }
        
        public static Int64 CreateCallbackData(string name, [CanBeNull] Action<string> onSuccess, [CanBeNull] Action<string> onError)
        {
            var ptr = _nextCallbackId++;

            lock (_callbacks) {
                _callbacks[ptr] = new XsollaClientCallback(OnCallbackResult, ptr);
            }

            return ptr;

            void OnCallbackResult(string result, string error) => RunOnStartThread.Run(() =>
            {
                XsollaLogger.Debug(Tag, $"{name} callback fire result={LogObfuscation.ObfuscateJsonIfEnabled(result)} error={error}");

                if (result != null)
                {
                    XsollaLogger.Debug(Tag, $"{name} result={LogObfuscation.ObfuscateJsonIfEnabled(result)}");
                    onSuccess?.Invoke(result);
                }
                else
                {
                    XsollaLogger.Error(Tag, $"{name} failed={error}");
                    onError?.Invoke(error);
                }
            });
        }
        
        

        public static XsollaClientCallback CreateCommonListener(string name, [CanBeNull] XsollaClientCallback.CallbackDelegate callback = default)
        {
            var ptr = _nextCallbackId++;
            var listener = new XsollaClientCallback(callback ?? OnCallbackResult, ptr);

            lock (_listeners) {
                _listeners[ptr] = listener;
            }

            return listener;
            
            void OnCallbackResult(string result, string error)  
            {
                XsollaLogger.Debug(Tag, $"{name} common callback fire result={LogObfuscation.ObfuscateJsonIfEnabled(result)} error={error}");
            }
        }
        
        public static void AddCommonListenerCallback(
            XsollaClientCallback listener, 
            string name, Action<string> onSuccess, Action<string> onError, [CanBeNull] XsollaClientCallback.ListenerDelegate onValidate = default
        ) {
            listener.AddListenerCallback(Tag, name, onSuccess, onError, onValidate);
        }
        
        #endregion

        public static void Clear()
        {
            // clear internal
            lock (_callbacks) { _callbacks.Clear(); }
            lock (_listeners) { _listeners.Clear(); }
        }
        
    }
}
#endif