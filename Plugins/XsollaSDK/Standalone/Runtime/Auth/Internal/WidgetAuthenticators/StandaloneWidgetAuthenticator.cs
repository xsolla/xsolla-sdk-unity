using System;
using UnityEngine;
using Xsolla.Core;

namespace Xsolla.Auth
{
	internal class StandaloneWidgetAuthenticator : IWidgetAuthenticator
	{
		private const string STATE_MISMATCH_MESSAGE = "Refused the sign-in redirect: it does not carry the state this sign-in sent";

		/// <summary>
		/// The sign-in still waiting for its redirect, if any.
		/// </summary>
		/// <remarks>
		/// Only one may wait: an abandoned sign-in would otherwise also take the next sign-in's redirect.
		/// </remarks>
		private static StandaloneWidgetAuthenticator WaitingSignIn;

		private readonly Action OnSuccessCallback;
		private readonly Action<Error> OnErrorCallback;
		private readonly Action OnCancelCallback;
		private readonly string Locale;
		private readonly SdkType SdkType;
		private readonly XsollaSettings Settings;
		private string LaunchRedirectUrl;
		private string LaunchState;

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
			if (!TakeOverWaiting())
				return;

			string redirectUrl =  null;
			if (Application.isEditor && EditorProvider.Handler != null && !string.IsNullOrEmpty(EditorProvider.Handler.DeeplinkUrl))
				redirectUrl = EditorProvider.Handler.DeeplinkUrl;

			// Cache so the code-for-token exchange sends a byte-identical redirect_uri (OAuth2 mismatches surface as 010-023).
			LaunchRedirectUrl = RedirectUrlHelper.GetRedirectUrl(Settings, redirectUrl);

			LaunchState = OAuthState.Resolve(null);

			var url = new UrlBuilder("https://login-widget.xsolla.com/latest/")
				.AddProjectId(Settings.LoginId)
				.AddClientId(Settings.OAuthClientId)
				.AddResponseType("code")
				.AddState(LaunchState)
				.AddRedirectUri(LaunchRedirectUrl)
				.AddScope("offline")
				.AddLocale(Locale)
				.Build();

			XsollaWebBrowser.Open(url);
			SubscribeToBrowser();
			XsollaWebBrowser.InAppBrowser?.UpdateSize(820, 840);
		}

		/// <summary>
		/// Makes this the waiting sign-in, cancelling the one that waited before it.
		/// </summary>
		/// <returns>
		/// Whether this is still the waiting sign-in afterwards: the cancelled one's <c>onCancel</c> may start
		/// another sign-in, which then takes over and cancels this one.
		/// </returns>
		internal bool TakeOverWaiting()
		{
			var previous = WaitingSignIn;
			WaitingSignIn = this;

			if (previous != null && previous != this)
			{
				previous.StopWaiting();
				previous.OnCancelCallback?.Invoke();
			}

			return WaitingSignIn == this;
		}

		/// <summary>
		/// Forgets the waiting sign-in when Play starts.
		/// </summary>
		/// <remarks>
		/// With the Editor's domain reload off, statics outlive Play mode, and the first sign-in would cancel one
		/// from the previous session.
		/// </remarks>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ForgetWaitingSignIn()
		{
			WaitingSignIn = null;
		}

		private void StopWaiting()
		{
			UnsubscribeFromBrowser();

			if (WaitingSignIn == this)
				WaitingSignIn = null;
		}

		internal void OnBrowserUrlChange(string newUrl)
		{
			// Before any callback, which may start the next sign-in.
			StopWaiting();

			if (ParseUtils.TryGetValueFromUrl(newUrl, ParseParameter.code, out var parsedCode))
			{
				// The redirect listener takes a call from any local process, so only the state ties
				// the code to this sign-in.
				if (OAuthState.IsEchoed(LaunchState, newUrl))
				{
					XsollaAuth.ExchangeCodeToToken(
						Settings,
						parsedCode,
						() => OnSuccessCallback?.Invoke(),
						error => OnErrorCallback?.Invoke(error),
						redirectUri: LaunchRedirectUrl,
						sdkType: SdkType);
				}
				else
				{
					XDebug.LogWarning(STATE_MISMATCH_MESSAGE);
					OnErrorCallback?.Invoke(new Error(ErrorType.Undefined, errorMessage: STATE_MISMATCH_MESSAGE));
				}
			}
			else if (ParseUtils.TryGetValueFromUrl(newUrl, ParseParameter.token, out var parsedToken))
			{
				// No state check: the widget answers with a token only in its JWT mode, which never
				// echoes the state back.
				Settings.XsollaToken.Create(parsedToken, isBasedOnDeviceId: false);
				OnSuccessCallback?.Invoke();
			}
			else if (ParseUtils.TryGetValueFromUrl(newUrl, ParseParameter.error, out var parsedError))
			{
				// For example, the user cancelled at the social provider.
				ParseUtils.TryGetValueFromUrl(newUrl, ParseParameter.error_code, out var parsedErrorCode);
				var errorMessage = ParseUtils.TryGetValueFromUrl(newUrl, ParseParameter.error_description, out var parsedErrorDescription)
					? parsedErrorDescription
					: parsedError;

				XDebug.LogWarning($"The sign-in redirect carries an error: {errorMessage}");
				OnErrorCallback?.Invoke(new Error(ErrorType.Undefined, errorCode: parsedErrorCode, errorMessage: errorMessage));
			}

			XsollaWebBrowser.Close();
		}

		private void OnBrowserClose(BrowserCloseInfo info)
		{
			StopWaiting();
			OnCancelCallback?.Invoke();
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
