using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Common
{
    internal sealed class XsollaClientCallback {
        public readonly Int64 callbackId;
        
        public delegate void CallbackDelegate([CanBeNull] string result, [CanBeNull] string error);
        public delegate bool ListenerDelegate([CanBeNull] string result, [CanBeNull] string error);
        
        private readonly CallbackDelegate _callback;
        private Queue<ListenerDelegate> _callbacks;
        
        private const string Tag = "XsollaClientCallback";

        public XsollaClientCallback(CallbackDelegate callback) {
            _callback = callback;
            callbackId = default;
        }
        
        public XsollaClientCallback(CallbackDelegate callback, Int64 id) {
            _callback = callback;
            callbackId = id;
        }

        public void onSuccess(string result) => onResult(result, null);
        public void onError(string error) => onResult(null, error);
        public void onResult([CanBeNull] string result, [CanBeNull] string error) => RunAndDequeueCallbacks(result, error);
        
        public void AddCallback(ListenerDelegate callback) {
            if (_callbacks == null) {
                _callbacks = new Queue<ListenerDelegate>();
            }

            lock (_callbacks) {
                _callbacks.Enqueue(callback);    
            }
        }
        
        public void RunAndDequeueCallbacks([CanBeNull] string result, [CanBeNull] string error)
        {
            XsollaLogger.Debug(Tag, $"RunAndDequeueCallbacks {result} {error}");
            
            var fired = false;
            if (_callbacks != null) {
                Queue<ListenerDelegate> callbacksToRun = null;
                
                lock (_callbacks) {
                    XsollaLogger.Debug(Tag, $"RunAndDequeueCallbacks has callbacks {_callbacks.Count}");
                    if (_callbacks.Count > 0) {
                        callbacksToRun = _callbacks;
                        _callbacks = new Queue<ListenerDelegate>();
                    }
                }
                
                if (callbacksToRun != null)
                {
                    while (callbacksToRun.Count > 0) {
                        var callback = callbacksToRun.Dequeue();
                        
                        if (!callback(result, error)) {
                            lock (_callbacks) {
                                _callbacks.Enqueue(callback);    
                            }
                        }
                        else 
                            fired = true;
                    }
                }
            }
            
            XsollaLogger.Debug(Tag, $"RunAndDequeueCallbacks default {fired}");
            
            if (!fired)
                _callback?.Invoke(result, error);  
        }
    }

    internal static class XsollaClientCallbackExt {
        public static void AddListenerCallback(
            this XsollaClientCallback listener,
            string tag, 
            string name, Action<string> onSuccess, Action<string> onError,
            [CanBeNull] XsollaClientCallback.ListenerDelegate onValidate = default
        )
        {
            listener.AddCallback((result, error) =>
            {
                XsollaLogger.Debug(tag,$"{name} callback fire");

                if (onValidate != null && !onValidate(result, error))
                {
                    XsollaLogger.Warning(tag, $"{name} skip, not validated");
                    return false;
                }

                RunOnStartThread.Run(() =>
                {
                    if (result != null)
                    {
                        XsollaLogger.Debug(tag, $"{name} result: {result}");
                        onSuccess(result);
                    }
                    else
                    {
                        if (ShouldReportAsError(tag, error)) {
                            XsollaLogger.Error(tag, $"{name} failed: {error}");
                        } else {
                            XsollaLogger.Warning(tag, $"{name} failed: {error}");
                        }
                        onError(error ?? string.Empty);
                    }

                    static bool ShouldReportAsError(string tag, string err) {
                        return string.IsNullOrEmpty(err) || (
                            !CheckIfWarningAndroid() &&
                            !CheckIfWarningIOS()
                        );

                        bool CheckIfWarningAndroid() =>
                            tag.ToLower().Contains("android") &&
                            (err.Contains("cancelled") || err.Contains("canceled"));

                        bool CheckIfWarningIOS() => false;
                    }
                });

                return true;
            });
        }
    }
}
