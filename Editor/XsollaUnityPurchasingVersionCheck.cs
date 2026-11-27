#if !XSOLLA_SDK_UNITY_PURCHASING_DISABLE && (XSOLLA_SDK_UIAP_PRE_4_13 || XSOLLA_SDK_UIAP_5_OR_LATER)
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Xsolla.SDK.Store;

namespace Xsolla.SDK
{
    /// <summary>
    /// Warns once per editor session that the installed Unity IAP version disables the Xsolla Unity IAP integration.
    /// </summary>
    /// <remarks>
    /// <c>Xsolla.SDK.UnityPurchasing</c> compiles only against Unity IAP [4.13.0, 5.0.0) through its
    /// <c>XSOLLA_SDK_UIAP_4</c> constraint, so on any other version it leaves the build without a compile error.
    /// An assembly marked with <see cref="XsollaPurchasingIntegrationAttribute"/> may take its place. This warning keeps
    /// the drop from going unnoticed. The rest of the SDK is unaffected.
    /// </remarks>
    [InitializeOnLoad]
    internal static class XsollaUnityPurchasingVersionCheck
    {
        private const string ReportedKey = "Xsolla.SDK.UnityPurchasingVersionCheck.Reported";

#if XSOLLA_SDK_UIAP_PRE_4_13
        private const string Reason =
            "Unity IAP older than 4.13.0 is installed, so the Xsolla Unity IAP integration (XsollaPurchasingModule) is disabled. " +
            "Upgrade Unity IAP to 4.15.1 or a later 4.x release.";
#else
        private const string Reason =
            "Unity IAP 5.0 or later is installed. The Xsolla Unity IAP integration (XsollaPurchasingModule) supports Unity IAP 4.x only, so it is disabled. " +
            "The rest of the SDK works: make purchases through XsollaStoreClient, or stay on Unity IAP 4.x.";
#endif

        static XsollaUnityPurchasingVersionCheck()
        {
            if (!SessionState.GetBool(ReportedKey, false) && !IsIntegrationCompiled())
            {
                SessionState.SetBool(ReportedKey, true);
                Debug.LogWarning($"[XsollaSDK] {Reason} Define XSOLLA_SDK_UNITY_PURCHASING_DISABLE to silence this warning.");
            }
        }

        /// <summary>
        /// Whether an assembly marked with <see cref="XsollaPurchasingIntegrationAttribute"/> compiled for the editor.
        /// </summary>
        /// <remarks>
        /// The integration is detected by its assembly, not its package: it may come as plain assets from a
        /// <c>.unitypackage</c>, and an installed package still compiles out on Unity IAP versions outside its range.
        /// A compiled editor-compatible script assembly is loaded into the editor domain before
        /// <see cref="InitializeOnLoadAttribute"/> constructors run, and one excluded by its constraints is never built.
        /// </remarks>
        private static bool IsIntegrationCompiled()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Any(assembly => assembly.IsDefined(typeof(XsollaPurchasingIntegrationAttribute), false));
        }
    }
}
#endif
