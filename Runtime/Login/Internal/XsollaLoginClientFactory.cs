namespace Xsolla.SDK.Login
{
    internal static class XsollaLoginClientFactory
    {
        // Test seam: when set, Create() returns this instead of the compile-time platform impl.
        // Production never sets it (mirrors XsollaCatalog.PaginatedItemsRequester); it lets tests drive
        // XsollaLoginClient through XsollaLoginClientImplFake — or any IXsollaLoginClient double — with no
        // live backend. Main-thread only; install/uninstall around each test.
        internal static System.Func<IXsollaLoginClient> Override;

        public static IXsollaLoginClient Create()
        {
            if (Override != null)
                return Override();
        #if UNITY_IOS && !UNITY_EDITOR
            return new XsollaLoginClientImplIOS();
        #elif UNITY_ANDROID && !UNITY_EDITOR
            return new XsollaLoginClientImplAndroid();
        #elif UNITY_STANDALONE || UNITY_WEBGL || UNITY_EDITOR //&& DISABLED_TMP
            return new XsollaLoginClientImplStandalone();
        #else
            return new XsollaLoginClientImplFake();
        #endif
        }
    }
}