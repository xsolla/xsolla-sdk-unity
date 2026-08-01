#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

namespace Xsolla.SDK.UnityPurchasing
{
    internal sealed class XsollaPurchasingStoreWrapper : IStoreWrapper
    {
        private readonly XsollaPurchasingStore _store;

        internal XsollaPurchasingStoreWrapper(XsollaPurchasingStore store)
        {
            _store = store;
        }

        public UnityEngine.Purchasing.Extension.Store instance => _store;
        public string name => XsollaPurchasingStore.Name;
        public ConnectionState GetStoreConnectionState() => _store.ConnectionState;
    }
}
#endif
