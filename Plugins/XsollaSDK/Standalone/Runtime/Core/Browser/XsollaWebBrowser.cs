using System;
using UnityEngine;

namespace Xsolla.Core
{
	internal static class XsollaWebBrowser
	{
		private static IInAppBrowser _inAppBrowser;
		//private static GameObject _inAppBrowserGameObject;

		public static IInAppBrowser InAppBrowser
		{
			get
			{
				return null;
			}
		}

		/// <summary>
		/// Opens Pay Station for <paramref name="paymentToken"/>: on a WebGL build with the in-app browser on, as the widget
		/// inside the page, otherwise in a browser window or tab.
		/// </summary>
		/// <returns>Whether Pay Station shows in the page, as the widget that reports its own close, rather than in a browser
		/// window or tab that reports nothing.</returns>
		public static bool OpenPurchaseUI(XsollaSettings settings, string paymentToken, bool forcePlatformBrowser = false, Action<BrowserCloseInfo> onBrowserClosed = null, PlatformSpecificAppearance platformSpecificAppearance = null, SdkType sdkType = SdkType.Store)
		{
			EventManagerProvider.Handler?.BroadcastEvent(EventTypes.PaystationOpen);
			
			var url = new PayStationUrlBuilder(settings, paymentToken, sdkType).Build(settings);
			var isInPage = Application.platform == RuntimePlatform.WebGLPlayer && !Application.isEditor && settings.InAppBrowserEnabled;
			if (isInPage)
			{
				XsollaWebBrowserHandlerWebGL.OpenPayStation(settings, paymentToken, onBrowserClosed, platformSpecificAppearance?.WebGlAppearance, externalUrl: url, sdkType);
			}
			else
			{
				XDebug.Log($"Purchase url: {LogRedaction.Redact(url)}");
				Open_(url, forcePlatformBrowser);
			}

			return isInPage;
		}
		
		public static void OpenWebshop(string url, bool forcePlatformBrowser = false)
		{
			XDebug.Log($"Webshop purchase url: {LogRedaction.Redact(url)}");

			EventManagerProvider.Handler?.BroadcastEvent(EventTypes.PaystationOpen);
			
			Open_(url, forcePlatformBrowser);
		}

		public static void Open(string url, bool forcePlatformBrowser = false)
		{
			Open_(url, forcePlatformBrowser);
		}
		
		static void Open_(string url, bool forcePlatformBrowser = false)
		{
			if (Application.platform == RuntimePlatform.WebGLPlayer && !Application.isEditor)
			{
				XsollaWebBrowserHandlerWebGL.OpenUrlInNewTab(url);
				return;
			}

			Application.OpenURL(url);
		}

		public static void Close(float delay = 0, bool isManually = false)
		{
			_inAppBrowser?.Close(delay, isManually);

			//if (_inAppBrowserGameObject == null)
			//	Object.Destroy(_inAppBrowserGameObject);

			_inAppBrowser = null;
		}
	}
}