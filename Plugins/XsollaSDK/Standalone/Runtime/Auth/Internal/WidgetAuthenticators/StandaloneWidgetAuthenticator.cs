using System;
using UnityEngine;
using Xsolla.Core;

namespace Xsolla.Auth
{
	internal class StandaloneWidgetAuthenticator : IWidgetAuthenticator
	{
		private readonly Action OnSuccessCallback;
		private readonly Action<Error> OnErrorCallback;
		private readonly Action OnCancelCallback;
		private readonly string Locale;
		private readonly SdkType SdkType;
		private readonly XsollaSettings Settings;
		private string LaunchRedirectUrl;

		public StandaloneWidgetAuthenticator(XsollaSettings settings, Action onSuccessCallback, Action<Error> onErrorCallback, Action onCancelCallback, string locale, SdkType sdkType)
		{
			OnSuccessCallback = onSuccessCallback;
			OnErrorCallback = onErrorCallback;
			OnCancelCallback = onCancelCallback;
			Locale = locale;
			SdkType = sdkType;
			Settings = settings;
		}

		public void Launch()
		{
			string redirectUrl =  null;
			if (Application.isEditor && EditorProvider.Handler != null && !string.IsNullOrEmpty(EditorProvider.Handler.DeeplinkUrl))
				redirectUrl = EditorProvider.Handler.DeeplinkUrl;

			// Cache so the code-for-token exchange sends a byte-identical redirect_uri (OAuth2 mismatches surface as 010-023).
			LaunchRedirectUrl = RedirectUrlHelper.GetRedirectUrl(Settings, redirectUrl);

			var url = new UrlBuilder("https://login-widget.xsolla.com/latest/")
				.AddProjectId(Settings.LoginId)
				.AddClientId(Settings.OAuthClientId)
				.AddResponseType("code")
				.AddState("xsollatest")
				.AddRedirectUri(LaunchRedirectUrl)
				.AddScope("offline")
				.AddLocale(Locale)
				.Build();

			XsollaWebBrowser.Open(url);
			SubscribeToBrowser();
			XsollaWebBrowser.InAppBrowser?.UpdateSize(820, 840);
		}

		private void OnBrowserUrlChange(string newUrl)
		{
			if (ParseUtils.TryGetValueFromUrl(newUrl, ParseParameter.code, out var parsedCode))
			{
				XsollaAuth.ExchangeCodeToToken(
					Settings,
					parsedCode,
					() => OnSuccessCallback?.Invoke(),
					error => OnErrorCallback?.Invoke(error),
					redirectUri: LaunchRedirectUrl,
					sdkType: SdkType);
			}
			else if (ParseUtils.TryGetValueFromUrl(newUrl, ParseParameter.token, out var parsedToken))
			{
				Settings.XsollaToken.Create(parsedToken, isBasedOnDeviceId: false);
				OnSuccessCallback?.Invoke();
			}
			
			UnsubscribeFromBrowser();
			XsollaWebBrowser.Close();
		}

		private void OnBrowserClose(BrowserCloseInfo info)
		{
			OnCancelCallback?.Invoke();
			UnsubscribeFromBrowser();
		}

		private void SubscribeToBrowser()
		{
			if (Application.isEditor && EditorProvider.Handler != null)
				EditorProvider.Handler.SubscribeOnDeeplinkEvent(OnBrowserUrlChange);
			
			var browser = XsollaWebBrowser.InAppBrowser;
			if (browser != null)
			{
				browser.CloseEvent += OnBrowserClose;
				browser.UrlChangeEvent += OnBrowserUrlChange;
			}
		}

		private void UnsubscribeFromBrowser()
		{
			if (Application.isEditor && EditorProvider.Handler != null)
				EditorProvider.Handler.UnsubscribeOnDeeplinkEvent(OnBrowserUrlChange);
			
			var browser = XsollaWebBrowser.InAppBrowser;
			if (browser != null)
			{
				browser.CloseEvent -= OnBrowserClose;
				browser.UrlChangeEvent -= OnBrowserUrlChange;
			}
		}
	}
}
