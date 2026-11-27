using System;
using System.Collections.Generic;

namespace Xsolla.Core
{
	internal abstract class OrderTracker
	{
		private bool _isCheckInProgress;
		private bool _isGetOrderIdInProgress;

		public readonly OrderTrackingData TrackingData;

		/// <summary>
		/// Whether the tracker was removed from tracking, set by <see cref="OrderTrackingService.RemoveOrderFromTracking"/>.
		/// <para/>
		/// A status request already in flight still answers after removal, so a terminal failure it reports is stale and
		/// must not answer the request a second time (<see cref="EndWithError"/>). Guarded by
		/// <see cref="OrderTrackingService.Sync"/>, as is every tracking state of the tracker.
		/// </summary>
		public bool IsRemoved { get; set; }

		/// <summary>
		/// Whether <see cref="Start"/> returned since the tracker was registered, set by <see cref="OrderTrackingService"/>.
		/// <para/>
		/// A tracker removed on another thread while it starts is stopped by the thread that starts it, once its start
		/// returns, so it does not stop before it starts and go on running untracked.
		/// </summary>
		public bool IsStarted { get; set; }

		/// <summary>
		/// The tracker that superseded this one under its order id or payment token, set by
		/// <see cref="OrderTrackingService"/>; null while this tracker is not superseded, and for a SKU.
		/// <para/>
		/// An order id or a payment token names one payment, which the successor watches as well, so a payment this tracker
		/// still reads, such as from a status request in flight, belongs to the newest tracker under the key
		/// (<see cref="EndWithSuccess"/>). A SKU names an item, so a tracker superseded under it watched another payment.
		/// </summary>
		public OrderTracker Successor { get; set; }

		/// <summary>
		/// Whether the tracker passed a payment on, guarded by <see cref="OrderTrackingService.Sync"/>. A tracker watches
		/// one payment, so it passes on one at most.
		/// </summary>
		private bool _isPaid;

		public abstract void Start();

		public abstract void Stop();

		protected void RemoveSelfFromTracking()
		{
			OrderTrackingService.RemoveOrderFromTracking(this);
		}

		/// <summary>
		/// Ends the tracker with a payment: the newest tracker of its <see cref="Successor"/> chain, this one when it is not
		/// superseded, leaves tracking and passes <paramref name="status"/> on, unless it already passed its payment on.
		/// <para/>
		/// A superseded tracker whose status request was in flight thus answers its successor's request, and drops the
		/// payment when the successor already reported it, so the payment is reported once. Leaving first keeps the registry
		/// consistent for a success handler that starts the purchase again under the same key. A tracker already removed,
		/// such as one whose request was answered at its time limit, leaves nothing, and its late payment arrives as a
		/// purchase without a request.
		/// <para/>
		/// The newest tracker is found and marked paid under <see cref="OrderTrackingService.Sync"/>, so reports of one
		/// payment racing on several threads pass it on once.
		/// </summary>
		protected void EndWithSuccess(OrderStatus status)
		{
			var tracker = this;
			bool isPaidBefore;
			var isStopDue = false;
			lock (OrderTrackingService.Sync)
			{
				while (tracker.Successor != null)
					tracker = tracker.Successor;

				isPaidBefore = tracker._isPaid;
				if (!isPaidBefore)
				{
					tracker._isPaid = true;
					OrderTrackingService.Unregister(tracker, out isStopDue);
				}
			}

			if (isPaidBefore)
			{
				XDebug.Log(tracker.TrackingData.settings, "Order tracker already passed its payment on, a second report of it is dropped");
			}
			else
			{
				if (isStopDue)
					tracker.Stop();

				tracker.TrackingData?.successCallback?.Invoke(status);
			}
		}

		/// <summary>
		/// Ends the tracker with a failure: leaves tracking, then answers its request with <paramref name="error"/>.
		/// <para/>
		/// Leaving first keeps the registry consistent for a failure handler that starts the purchase again under the same
		/// key. A tracker already removed had its request answered, or deliberately left unanswered, by whoever removed it,
		/// so it drops the failure and leaves nothing, which would otherwise remove its successor under that key. Failures
		/// racing on several threads thus answer once.
		/// </summary>
		internal void EndWithError(Error error)
		{
			bool isRemovedBefore;
			var isStopDue = false;
			lock (OrderTrackingService.Sync)
			{
				isRemovedBefore = IsRemoved;
				if (!isRemovedBefore)
					OrderTrackingService.Unregister(this, out isStopDue);
			}

			if (isRemovedBefore)
			{
				XDebug.Log(TrackingData.settings, $"Order tracker already removed from tracking, its {error.ErrorType} failure is dropped");
			}
			else
			{
				if (isStopDue)
					Stop();

				TrackingData?.errorCallback?.Invoke(error);
			}
		}

		/// <summary>
		/// Checks the order status once, if an order is known and no check is in flight; a missing user token fails the check
		/// before either.
		/// <para/>
		/// A check has three outcomes. A paid order (<c>done</c>) goes to <paramref name="onDone"/>. A terminal answer goes
		/// to <paramref name="onError"/>: a refunded order (<c>canceled</c>, <see cref="ErrorType.OrderCanceled"/>), the
		/// stored error of a paid payment token whose order or invoice id is invalid, or a request the server refuses
		/// (<see cref="IsRefusedByServer"/>). A check that cannot run (no user token) or whose request fails otherwise goes
		/// to <paramref name="onCheckFailed"/>: it says nothing about the payment, so the caller checks again. Every other
		/// status answers nothing.
		/// </summary>
		protected void CheckOrderStatus(Action<OrderStatus> onDone, Action<Error> onError, Action<Error> onCheckFailed)
		{
			if (!TrackingData.settings.XsollaToken.Exists)
			{
				onCheckFailed?.Invoke(new Error(ErrorType.Unauthorized, errorMessage: "No user token for the order status check"));
				return;
			}

			if (!CheckOrderIdExists())
				return;

			// Read together: a payment token tracker learns an invalid order id as 0 along with its error.
			long orderId;
			Error orderIdError;
			lock (OrderTrackingService.Sync)
			{
				orderId = TrackingData.OrderId;
				orderIdError = TrackingData.Error;
			}

			// A SKU-only tracker has no order to query. Only an events tracker serves a web shop purchase, and it polls no order.
			if (orderId == -1)
				return;
			
			if (_isCheckInProgress) // Prevent double check
				return;
			
			if (orderId == 0 && orderIdError != null)
			{
				onError?.Invoke(orderIdError);
				return;
			}

			_isCheckInProgress = true;

			OrderStatusService.GetOrderStatus(
				TrackingData.settings,
				orderId,
				status =>
				{
					_isCheckInProgress = false;
					HandleOrderStatus(status, onDone, onError);
				},
				error =>
				{
					_isCheckInProgress = false;
					if (IsRefusedByServer(error))
						onError?.Invoke(error);
					else
						onCheckFailed?.Invoke(error);
				},
				TrackingData.sdkType,
				token: TrackingData.token
			);
		}
		
		/// <summary>
		/// Whether the tracker knows its order, or watches a SKU only. A payment token tracker requests its order id
		/// instead, once at a time: the events updater checks its trackers on the thread of each events response, and
		/// responses can overlap.
		/// </summary>
		protected bool CheckOrderIdExists()
		{
			bool isOrderIdUnknown;
			var isOrderIdRequestDue = false;
			lock (OrderTrackingService.Sync)
			{
				isOrderIdUnknown = TrackingData.OrderId == -1;
				if (isOrderIdUnknown && !string.IsNullOrEmpty(TrackingData.token) && !_isGetOrderIdInProgress)
				{
					_isGetOrderIdInProgress = true;
					isOrderIdRequestDue = true;
				}
			}

			if (isOrderIdUnknown && string.IsNullOrEmpty(TrackingData.sku) && string.IsNullOrEmpty(TrackingData.token))
			{
				XDebug.LogError(TrackingData.settings, "Order ID is not set and no token provided. Check cancelled");
				return false;
			}
			
			if (isOrderIdUnknown && !string.IsNullOrEmpty(TrackingData.token))
			{
				if (isOrderIdRequestDue)
				{
					OrderStatusService.GetOrderInfo(
						TrackingData.settings,
						accessToken: TrackingData.token,
						TrackingData.sdkType,
						onSuccess: orderInfo =>
						{
							lock (OrderTrackingService.Sync)
							{
								_isGetOrderIdInProgress = false;
								if (orderInfo.TryAsDone(out var done))
									TrackingData.OrderId = done.orderId;
							}
						},
						onFailure: error =>
						{
							lock (OrderTrackingService.Sync)
							{
								_isGetOrderIdInProgress = false;
								if (error.ErrorType == ErrorType.OrderInfoDoneButInvalidOrderId ||
								    error.ErrorType == ErrorType.OrderInfoDoneButInvalidInvoiceId)
								{
									TrackingData.OrderId = 0;
									TrackingData.Error = error;
								}
							}
						}
					);
				}

				return false;
			}

			return true;
		}

		private void HandleOrderStatus(OrderStatus status, Action<OrderStatus> onDone, Action<Error> onError)
		{
			switch (status.status)
			{
				case "done":
					onDone?.Invoke(status);
					break;
				case "canceled":
					onError?.Invoke(new Error(ErrorType.OrderCanceled, errorMessage: $"Order {status.order_id} was canceled: its payment was refunded"));
					break;
			}
		}

		/// <summary>
		/// Whether a failed order status request is refused by the server, which no later check changes: an unknown order
		/// (<see cref="ErrorType.OrderNotFound"/>, HTTP 404), a user token still rejected once
		/// <see cref="TokenAutoRefresher"/> could not renew it, a project the server reports as misconfigured
		/// (<see cref="ErrorType.InvalidProjectSettings"/>), or another client error (HTTP 4xx, 405 included) other than a
		/// timeout (408) or a rate limit (429).
		/// <para/>
		/// A network error, a server error (5xx), a timeout, a rate limit, or an answer without a status code says nothing
		/// about the order, so the check runs again. A new order reads <c>new</c> from its first check, not 404, so a 404 is
		/// final at once. The payment status of a payment token is not judged here: it is 404 until the token has an
		/// invoice.
		/// </summary>
		private static bool IsRefusedByServer(Error error)
		{
			switch (error.ErrorType)
			{
				case ErrorType.OrderNotFound:
				case ErrorType.InvalidToken:
				case ErrorType.Unauthorized:
				case ErrorType.MethodIsNotAllowed:
				case ErrorType.InvalidProjectSettings:
					return true;
				case ErrorType.UnknownError:
				case ErrorType.Undefined:
					return int.TryParse(error.statusCode, out var statusCode)
						&& statusCode >= 400 && statusCode < 500
						&& statusCode != 408 && statusCode != 429;
				default:
					return false;
			}
		}

		protected OrderTracker(OrderTrackingData trackingData)
		{
			TrackingData = trackingData;
		}
	}
}