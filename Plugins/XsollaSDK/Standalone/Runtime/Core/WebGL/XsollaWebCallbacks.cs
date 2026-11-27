using System;
using UnityEngine;

namespace Xsolla.Core
{
	internal class XsollaWebCallbacks : MonoBehaviour
	{
		private event Action OnPaymentStatusUpdate;
		private event Action OnPaymentCancel;
		private event Action OnPaymentClose;

		public event Action<string> WidgetAuthSuccess;
		public event Action WidgetAuthCancel;

		public event Action<XsollaSettings, string, string> WidgetOpen;

		private void Awake()
		{
			DontDestroyOnLoad(gameObject);
		}

		// Callback for Xsolla Pay Station (do not remove)
		public void PublishPaymentStatusUpdate()
		{
			OnPaymentStatusUpdate?.Invoke();
		}

		// Callback for Xsolla Pay Station (do not remove)
		public void PublishPaymentCancel()
		{
			OnPaymentCancel?.Invoke();
		}

		/// <summary>
		/// Callback for Xsolla Pay Station (do not remove): the in-page widget left the page without a cancel, after a
		/// status message or once the payment is done.
		/// </summary>
		public void PublishPaymentClose()
		{
			OnPaymentClose?.Invoke();
		}

		// Callback for Xsolla Widget (do not remove)
		public void PublishWidgetAuthSuccess(string data)
		{
			WidgetAuthSuccess?.Invoke(data);
		}

		// Callback for Xsolla Widget (do not remove)
		public void PublishWidgetAuthCancel()
		{
			WidgetAuthCancel?.Invoke();
		}

		public static void AddPaymentStatusUpdateHandler(Action action)
		{
			Instance.OnPaymentStatusUpdate += action;
		}

		public static void RemovePaymentStatusUpdateHandler(Action action)
		{
			Instance.OnPaymentStatusUpdate -= action;
		}

		public static void AddPaymentCancelHandler(Action action)
		{
			Instance.OnPaymentCancel += action;
		}

		public static void RemovePaymentCancelHandler(Action action)
		{
			Instance.OnPaymentCancel -= action;
		}

		public static void AddPaymentCloseHandler(Action action)
		{
			Instance.OnPaymentClose += action;
		}

		public static void RemovePaymentCloseHandler(Action action)
		{
			Instance.OnPaymentClose -= action;
		}
		
		public void InvokeOpenPopup(XsollaSettings settings, string payload)
		{
			var parts = payload.Split('|');
			if (parts.Length < 2)
			{
				Debug.LogError("Invalid payload from JS: " + payload);
				return;
			}

			WidgetOpen?.Invoke(settings, parts[0], parts[1]);
		}

		#region Singleton

		private static XsollaWebCallbacks _instance;

		public static XsollaWebCallbacks Instance
		{
			get
			{
				if (_instance == null)
					_instance = new GameObject("XsollaWebCallbacks").AddComponent<XsollaWebCallbacks>();

				return _instance;
			}
		}

		#endregion
	}
}