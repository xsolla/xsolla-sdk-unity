using System.Collections;
using UnityEngine;

namespace Xsolla.Core
{
	internal class OrderTrackerByShortPolling : OrderTracker
	{
		private Coroutine checkStatusCoroutine;
		
		private OrderTrackerByPaystationCallbacksHelper _webHelper = null;

		public OrderTrackerByShortPolling(OrderTrackingData trackingData) : base(trackingData)
		{
			if (Application.platform == RuntimePlatform.WebGLPlayer)
				_webHelper = new OrderTrackerByPaystationCallbacksHelper();
		}

		public override void Start()
		{
			_webHelper?.Start(
				onOk: () => { /* do nothing */ }, 
				onCancel: () => {
					HandleError(new Error(ErrorType.UserCancelled));
				}
			);

			checkStatusCoroutine = CoroutinesExecutor.Run(TrackOrderStatus());
		}

		public override void Stop()
		{
			_webHelper?.Stop();

			CoroutinesExecutor.Stop(checkStatusCoroutine);
		}

		private IEnumerator TrackOrderStatus()
		{
			var timeLimit = Time.realtimeSinceStartup + Constants.SHORT_POLLING_LIMIT;

			while (true)
			{
				yield return new WaitForSecondsRealtime(Constants.SHORT_POLLING_INTERVAL);
				CheckOrderStatus(
					onDone: (status) => HandleOrderDone(status), 
					onCancel: () => RemoveSelfFromTracking(), 
					onError: (error) => HandleError(error));

				if (Time.realtimeSinceStartup > timeLimit)
				{
					HandleError(new Error(ErrorType.TimeLimitReached, errorMessage: "Polling time limit reached"));
					break;
				}
			}
		}

		private void HandleOrderDone(OrderStatus status)
		{
			TrackingData?.successCallback?.Invoke(status);
			RemoveSelfFromTracking();
		}

		private void HandleError(Error error)
		{
			TrackingData?.errorCallback?.Invoke(error);
			RemoveSelfFromTracking();
		}
	}
}