using System.Collections;
using UnityEngine;

namespace Xsolla.Core
{
	internal class OrderTrackerByShortPolling : OrderTracker
	{
		/// <summary>
		/// How long a tracker polls before it fails its request with <see cref="ErrorType.TimeLimitReached"/>.
		/// <para/>
		/// Production keeps <see cref="Constants.SHORT_POLLING_LIMIT"/>. Tests shorten it to reach the limit without
		/// waiting it out.
		/// </summary>
		internal static float TimeLimitSeconds = Constants.SHORT_POLLING_LIMIT;

		private Coroutine checkStatusCoroutine;

		/// <summary>
		/// The latest status check that could not run or whose request failed, named by the time limit failure.
		/// <para/>
		/// A failed check says nothing about the payment, so polling goes on until the time limit, which is the retry
		/// budget: the user may still be paying, and a network blip must not fail the purchase.
		/// </summary>
		private Error _lastCheckFailure;
		
		private OrderTrackerByPaystationCallbacksHelper _webHelper = null;

		/// <summary>
		/// Whether Pay Station shows this purchase in the page, as the WebGL widget that reports its own close.
		/// <para/>
		/// The user may take any time in the open widget, such as on a 3-D Secure check, and the widget is theirs to
		/// close, so its time limit starts only when it closes (<see cref="HandleWidgetClose"/>).
		/// </summary>
		private readonly bool _isPurchaseUIInPage;

		/// <summary>
		/// The <see cref="Time.realtimeSinceStartup"/> past which polling fails the request with
		/// <see cref="ErrorType.TimeLimitReached"/>; infinite until the time limit starts.
		/// </summary>
		private float _timeLimitTime = float.PositiveInfinity;

		public OrderTrackerByShortPolling(OrderTrackingData trackingData, bool isPurchaseUIInPage) : base(trackingData)
		{
			_isPurchaseUIInPage = isPurchaseUIInPage;
			if (Application.platform == RuntimePlatform.WebGLPlayer)
				_webHelper = new OrderTrackerByPaystationCallbacksHelper();
		}

		public override void Start()
		{
			_webHelper?.Start(
				onOk: () => { /* do nothing */ },
				onCancel: () => {
					EndWithError(new Error(ErrorType.UserCancelled));
				},
				onClose: () => HandleWidgetClose(Time.realtimeSinceStartup)
			);

			StartTimeLimit(Time.realtimeSinceStartup);
			checkStatusCoroutine = CoroutinesExecutor.Run(TrackOrderStatus());
		}

		public override void Stop()
		{
			_webHelper?.Stop();

			CoroutinesExecutor.Stop(checkStatusCoroutine);
		}

		/// <summary>
		/// Starts the time limit at <paramref name="now"/>, unless the purchase is in the page, whose time limit starts when
		/// its widget closes.
		/// </summary>
		internal void StartTimeLimit(float now)
		{
			if (!_isPurchaseUIInPage)
				_timeLimitTime = now + TimeLimitSeconds;
		}

		/// <summary>
		/// Starts the time limit at <paramref name="now"/>, when the in-page widget closes without a cancel, unless it already
		/// runs.
		/// <para/>
		/// The order may still resolve after the widget is gone, such as a payment confirmed late, so polling goes on for the
		/// time limit.
		/// </summary>
		internal void HandleWidgetClose(float now)
		{
			if (float.IsPositiveInfinity(_timeLimitTime))
				_timeLimitTime = now + TimeLimitSeconds;
		}

		private IEnumerator TrackOrderStatus()
		{
			do
			{
				yield return new WaitForSecondsRealtime(Constants.SHORT_POLLING_INTERVAL);
			}
			while (!Poll(Time.realtimeSinceStartup));
		}

		/// <summary>
		/// Checks the order status once, then fails the request with <see cref="ErrorType.TimeLimitReached"/> when
		/// <paramref name="now"/> is past the time limit.
		/// </summary>
		/// <returns>Whether the time limit is reached, which ends polling.</returns>
		internal bool Poll(float now)
		{
			CheckOrderStatus(
				onDone: EndWithSuccess,
				onError: EndWithError,
				onCheckFailed: RecordCheckFailure);

			var isTimeLimitReached = now > _timeLimitTime;
			if (isTimeLimitReached)
			{
				var message = _lastCheckFailure == null
					? "Polling time limit reached"
					: $"Polling time limit reached; last failed status check: {_lastCheckFailure}";
				EndWithError(new Error(ErrorType.TimeLimitReached, errorMessage: message));
			}

			return isTimeLimitReached;
		}

		/// <summary>
		/// Keeps <paramref name="error"/> as the last check failure and logs it: the first failure of the tracker as a
		/// warning, the repeats at debug level, since a lasting outage fails every poll until the time limit.
		/// </summary>
		private void RecordCheckFailure(Error error)
		{
			if (_lastCheckFailure == null)
				XDebug.LogWarning(TrackingData.settings, $"Order status check failed, polling goes on until the time limit: {error}");
			else
				XDebug.LogDebug(TrackingData.settings, $"Order status check failed again: {error}");

			_lastCheckFailure = error;
		}
	}
}