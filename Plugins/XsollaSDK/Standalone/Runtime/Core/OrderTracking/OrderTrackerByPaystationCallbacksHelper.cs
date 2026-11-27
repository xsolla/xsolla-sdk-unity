using System;

namespace Xsolla.Core
{
	internal class OrderTrackerByPaystationCallbacksHelper
	{
		private bool isCancelByUser;
		private Action handleOK;
		private Action handleCancel;
		private Action _cancelHandlerWrapper;

		private Action _handleClose;

		/// <summary>
		/// Subscribes to the messages of the in-page Pay Station widget: <paramref name="onOk"/> for a status message,
		/// <paramref name="onCancel"/> for a close without one, and <paramref name="onClose"/>, when given, for the widget
		/// leaving the page without a cancel.
		/// </summary>
		public void Start(Action onOk, Action onCancel, Action onClose = null)
		{
			handleOK = onOk;
			handleCancel = onCancel;
			_handleClose = onClose;

			XsollaWebCallbacks.AddPaymentStatusUpdateHandler(handleOK);
			_cancelHandlerWrapper = () =>
			{
				isCancelByUser = true;
				handleCancel?.Invoke();
			};
			XsollaWebCallbacks.AddPaymentCancelHandler(_cancelHandlerWrapper);
			XsollaWebCallbacks.AddPaymentCloseHandler(_handleClose);
		}

		public void Stop()
		{
			XsollaWebCallbacks.RemovePaymentStatusUpdateHandler(handleOK);
			XsollaWebCallbacks.RemovePaymentCancelHandler(_cancelHandlerWrapper);
			XsollaWebCallbacks.RemovePaymentCloseHandler(_handleClose);
			XsollaWebBrowserHandlerWebGL.ClosePayStation(isCancelByUser);
		}
	}
}
