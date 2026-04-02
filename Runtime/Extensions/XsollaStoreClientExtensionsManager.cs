using System;

// For the order tracking cancellation.
using Xsolla.SDK.Store;

namespace Xsolla.SDK.Extensions
{
    public class XsollaStoreClientExtensionsManager
    {
        public XsollaStoreClientExtensionsSettings Settings { get; private set; }
        public bool IsStarted { get; private set; }
        private XsollaStoreClientEventManager _eventManager;
        
        private static XsollaStoreClientExtensionsManager _instance;
        internal static XsollaStoreClientExtensionsManager Instance() {
            if (_instance != null)
                return _instance;
            
            _instance = new XsollaStoreClientExtensionsManager();
            return _instance;
        }

        
        protected XsollaStoreClientExtensionsManager() {}

        public void Start()
        {
            if (IsStarted) return;
            
            XsollaStoreExtensionsProviderFactory.Register();

            if (Settings.eventsCallback != null)
            {
                _eventManager = XsollaStoreClientEventManager.Builder.Create()
                    .SetOnEventCallback(Settings.eventsCallback)
                    .Start();
            }

            IsStarted = true;
        }
        
        public void Stop()
        {
            if (!IsStarted) return;
            
            XsollaStoreExtensionsProviderFactory.Unregister();

            if (_eventManager != null)
            {
                _eventManager.Stop();
                _eventManager = null;
            }

            IsStarted = false;
        }

        public void StopAllOrdersFromTracking()
        {
#if UNITY_STANDALONE || UNITY_WEBGL
            Xsolla.Core.OrderTrackingService.CancelAllOrdersFromTracking();
#elif UNITY_ANDROID
            XsollaStoreClientImplAndroid.CancelActivePurchase();
#elif UNITY_IOS
            XsollaStoreClientImplIOS.CancelTransaction();
#endif
        }
        
        private void SetSettings(XsollaStoreClientExtensionsSettings settings)
        {
            Settings = settings;
        }

        public class Builder
        {
            protected readonly XsollaStoreClientExtensionsManager _manager = Instance();
            
            public Builder SetSettings(XsollaStoreClientExtensionsSettings settings) { _manager.SetSettings(settings); return this; }
            
            public static Builder Create()
            {
                if (_instance != null)
                    throw new Exception("XsollaStoreClientExtensionsManager already exists");
                
                return new Builder();
            }

            public XsollaStoreClientExtensionsManager Build() => _manager;
        }
    }
}