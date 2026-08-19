namespace Xsolla.SDK.Store
{
    internal static class XsollaStoreClientFactory
    {
        // Test seam: when set, Create() returns this instead of the compile-time platform impl.
        // Production never sets it (mirrors XsollaCatalog.PaginatedItemsRequester); it lets tests drive
        // XsollaStoreClient through XsollaStoreClientImplFake — or any IXsollaStoreClient double — with no
        // live backend. Main-thread only; install/uninstall around each test.
        internal static System.Func<IXsollaStoreClient> Override;

        public static IXsollaStoreClient Create()
        {
            if (Override != null)
                return Override();
        #if UNITY_IOS && !UNITY_EDITOR
            return new XsollaStoreClientImplIOS();
        #elif UNITY_ANDROID && !UNITY_EDITOR
            return new XsollaStoreClientImplAndroid();
        #elif UNITY_STANDALONE || UNITY_WEBGL || UNITY_EDITOR //&& DISABLED_TMP
            return new XsollaStoreClientImplStandalone();
        #else
            return new XsollaStoreClientImplFake();
        #endif
        }
    }
}