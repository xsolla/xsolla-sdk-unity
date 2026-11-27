using System;
using System.Collections.Generic;
using Xsolla.GetUpdates;

namespace Xsolla.Core
{
	internal static class OrderTrackingService
	{
		/// <summary>
		/// Guards the registry and the tracking state of its trackers: <see cref="OrderTracker.IsRemoved"/>,
		/// <see cref="OrderTracker.IsStarted"/>, <see cref="OrderTracker.Successor"/>, whether a tracker passed its
		/// payment on, whether it requests its order id, and the order id and its error learned from a payment token. It
		/// also guards the events updaters of <see cref="OrderTrackerByEventsUpdater"/> and the trackers each one serves.
		/// <para/>
		/// The plain .NET SDK runs coroutines and request callbacks on worker threads. The state is decided under the lock
		/// and acted on outside it: trackers start and stop, and requests are answered, without holding it, so a handler
		/// that starts or ends a purchase on another thread cannot deadlock against it.
		/// </summary>
		internal static readonly object Sync = new object();

		private static readonly Dictionary<long, OrderTracker> Trackers = new Dictionary<long, OrderTracker>();
		private static readonly Dictionary<string, OrderTracker> TrackersBySku = new Dictionary<string, OrderTracker>();
		private static readonly Dictionary<string, OrderTracker> TrackersByToken = new Dictionary<string, OrderTracker>();

		public static void SetUnorderedPurchaseEvent(XsollaSettings settings, Action<PaymentEvent[]> onEvent, int intervalSec)
		{
			OrderTrackerByEventsUpdater.StartEndlessUpdater(settings, intervalSec, onEvent);
		}

		/// <summary>
		/// Starts status tracking for the specified order, by Events API updates or by short polling
		/// (<see cref="CreateTracker"/>).
		/// </summary>
		/// <param name="orderId">Order ID.</param>
		/// <param name="isUserInvolvedToPayment">Unused: every purchase is tracked the same way (<see cref="CreateTracker"/>).</param>
		/// <param name="onSuccess">Callback, triggered when the order status is changed to `done`</param>
		/// <param name="onError">Callback, triggered when an error occurs during the order tracking.</param>
		/// <param name="sdkType">SDK type. Used for internal analytics.</param>
		/// <param name="isPurchaseUIInPage">Whether Pay Station shows the purchase in the page, as the WebGL widget
		/// (<see cref="XsollaWebBrowser.OpenPurchaseUI"/>).</param>
		public static void AddOrderForTracking(XsollaSettings settings, long orderId, bool isUserInvolvedToPayment, Action<OrderStatus> onSuccess, Action<Error> onError, SdkType sdkType = SdkType.Store, bool isPurchaseUIInPage = false)
		{
			StartTracker(CreateTracker(settings, orderId, sku: null, token: null, isPurchaseUIInPage, onSuccess, onError, sdkType));
		}
		
		public static void AddOrderForTrackingBySku(XsollaSettings settings, string sku, bool isUserInvolvedToPayment, Action<OrderStatus> onSuccess, Action<Error> onError, SdkType sdkType = SdkType.Store)
		{
			StartTracker(CreateTracker(settings, orderId: -1, sku,  token: null, isPurchaseUIInPage: false, onSuccess, onError, sdkType));
		}
		
		public static void AddOrderForTrackingByPaymentToken(XsollaSettings settings, string token, bool isUserInvolvedToPayment, Action<OrderStatus> onSuccess, Action<Error> onError, SdkType sdkType = SdkType.Store, bool isPurchaseUIInPage = false)
		{
			StartTracker(CreateTracker(settings, orderId: -1, sku: null,  token: token, isPurchaseUIInPage, onSuccess, onError, sdkType));
		}

		/// <summary>
		/// Creates the tracker of a purchase: by Events API updates when <see cref="XsollaSettings.EventApiEnabled"/>,
		/// otherwise by short polling.
		/// <para/>
		/// One polling tracker serves every page kind, the WebGL in-page widget included. Unity's web checkout never trusts
		/// a page-sent status either: it reads the order from the backend until it resolves. A page-sent status can arrive
		/// before the order or the payment token resolves, and nothing sends it again, while polling reads until the order
		/// resolves or the tracking time limit. The widget's cancel still answers the purchase at once, and the time limit of
		/// a purchase in the widget starts only when the widget closes (<see cref="OrderTrackerByShortPolling"/>).
		/// </summary>
		private static OrderTracker CreateTracker(XsollaSettings settings, long orderId, string sku, string token, bool isPurchaseUIInPage, Action<OrderStatus> onSuccess, Action<Error> onError, SdkType sdkType)
		{
			var trackingData = new OrderTrackingData(settings, orderId, sku, token, onSuccess, onError, sdkType);

			return settings.EventApiEnabled
				? (OrderTracker)new OrderTrackerByEvents(trackingData)
				: (OrderTracker)new OrderTrackerByShortPolling(trackingData, isPurchaseUIInPage);
		}

		/// <summary>
		/// Stops status tracking for all orders and fails their requests with <see cref="ErrorType.Aborted"/>.
		/// <para/>
		/// Cancels the trackers registered when it is called. A failure handler that starts a purchase again registers a
		/// new tracker, which stays tracked. If that purchase supersedes a tracker still to be cancelled, the superseded
		/// tracker is skipped: its request was already answered. Every events tracker is registered here, so cancelling
		/// them leaves no tracker in the events updaters.
		/// </summary>
		public static void CancelAllOrdersFromTracking()
		{
			List<OrderTracker> trackers;
			lock (Sync)
			{
				trackers = new List<OrderTracker>(Trackers.Values);
				trackers.AddRange(TrackersBySku.Values);
				trackers.AddRange(TrackersByToken.Values);
			}

			foreach (var tracker in trackers)
				CancelOrderFromTracking(tracker);
		}
		
		/// <summary>
		/// Stops status tracking for all orders.
		/// </summary>
		public static void RemoveAllOrdersFromTracking(XsollaSettings settings)
		{
			OrderTrackerByEventsUpdater.RemoveAllFromTracking(settings);

			List<OrderTracker> trackers;
			lock (Sync)
			{
				trackers = new List<OrderTracker>(Trackers.Values);
				trackers.AddRange(TrackersBySku.Values);
				trackers.AddRange(TrackersByToken.Values);
			}

			foreach (var tracker in trackers)
				RemoveOrderFromTracking(tracker);
		}

		/// <summary>
		/// Stops status tracking for the orders of the session whose settings are <paramref name="sessionSettings"/>, the
		/// very instance its trackers carry, without answering their requests: the session answers them itself as it ends.
		/// <para/>
		/// Each session of a store client brings settings of its own, so the trackers of another session, such as another
		/// client's, stay tracked. A status request of a removed tracker still in flight can report its payment afterwards
		/// (<see cref="OrderTracker.EndWithSuccess"/>). The events updater of the session's project keeps its endless mode.
		/// </summary>
		public static void RemoveSessionOrdersFromTracking(XsollaSettings sessionSettings)
		{
			var stopDue = new List<OrderTracker>();
			lock (Sync)
			{
				var trackers = new List<OrderTracker>(Trackers.Values);
				trackers.AddRange(TrackersBySku.Values);
				trackers.AddRange(TrackersByToken.Values);

				foreach (var tracker in trackers)
				{
					if (ReferenceEquals(tracker.TrackingData.settings, sessionSettings) && Unregister(tracker, out var isStopDue) && isStopDue)
						stopDue.Add(tracker);
				}
			}

			foreach (var tracker in stopDue)
				tracker.Stop();
		}

		/// <summary>
		/// Stops status tracking for the specified order and fails its request with <see cref="ErrorType.Aborted"/>, if it
		/// is the tracker registered under its order id, SKU or payment token.
		/// <para/>
		/// A tracker no longer registered had its request answered, such as a superseded one, or answers it itself, such as
		/// one ending on another thread, so it is skipped. The request is answered after the removal, so a failure handler
		/// that starts the purchase again registers a new tracker that stays tracked.
		/// </summary>
		public static void CancelOrderFromTracking(OrderTracker tracker)
		{
			bool isRegistered;
			bool isStopDue;
			lock (Sync)
				isRegistered = Unregister(tracker, out isStopDue);

			if (isRegistered)
			{
				if (isStopDue)
					tracker.Stop();

				tracker.TrackingData.errorCallback?.Invoke(new Error(ErrorType.Aborted, errorMessage: "Order cancelled."));
			}
		}

		/// <summary>
		/// Stops status tracking for <paramref name="tracker"/>, if it is the tracker registered under its order id, SKU or
		/// payment token.
		/// <para/>
		/// A tracker no longer registered, such as a superseded one, removes nothing: the tracker under its key now is its
		/// successor. A payment token tracker also learns its order id, which must not remove an order tracker of that id.
		/// </summary>
		public static void RemoveOrderFromTracking(OrderTracker tracker)
		{
			bool isStopDue;
			lock (Sync)
				Unregister(tracker, out isStopDue);

			if (isStopDue)
				tracker.Stop();
		}

		/// <summary>
		/// Unregisters <paramref name="tracker"/> and marks it removed, if it is the tracker registered under its order id,
		/// SKU or payment token, as <see cref="RemoveOrderFromTracking"/> describes. Called under <see cref="Sync"/>.
		/// </summary>
		/// <param name="isStopDue">Whether the caller stops the tracker, once out of the lock: a tracker still starting
		/// is stopped by its start instead (<see cref="OrderTracker.IsStarted"/>).</param>
		/// <returns>Whether the tracker was registered.</returns>
		internal static bool Unregister(OrderTracker tracker, out bool isStopDue)
		{
			var trackingData = tracker.TrackingData;
			bool isRegistered;
			if (trackingData.orderId != -1 && Trackers.TryGetValue(trackingData.orderId, out var registered) && ReferenceEquals(registered, tracker))
			{
				Trackers.Remove(trackingData.orderId);
				isRegistered = true;
			}
			else if (trackingData.sku != null && TrackersBySku.TryGetValue(trackingData.sku, out var registeredBySku) && ReferenceEquals(registeredBySku, tracker))
			{
				TrackersBySku.Remove(trackingData.sku);
				isRegistered = true;
			}
			else if (trackingData.token != null && TrackersByToken.TryGetValue(trackingData.token, out var registeredByToken) && ReferenceEquals(registeredByToken, tracker))
			{
				TrackersByToken.Remove(trackingData.token);
				isRegistered = true;
			}
			else
			{
				isRegistered = false;
			}

			if (isRegistered)
				tracker.IsRemoved = true;

			isStopDue = isRegistered && tracker.IsStarted;
			return isRegistered;
		}

		public static void ReplaceTracker(OrderTracker oldTracker, OrderTracker newTracker)
		{
			RemoveOrderFromTracking(oldTracker);
			StartTracker(newTracker);
		}
		
		/// <summary>
		/// Registers <paramref name="tracker"/> under its order id, SKU or payment token and starts it. A tracker already
		/// registered under that key is superseded: it stops, and its request fails with <see cref="ErrorType.Aborted"/>.
		/// <para/>
		/// Closing a payment page sends no signal, so a new purchase under a tracked key, such as the same item bought again
		/// in a web shop or the same payment token opened again, is the only sign that the tracked purchase was left. Google
		/// Play ends one purchase flow before another can start. Refusing the new purchase instead would block the item until
		/// the tracked one times out. A payment made in the superseded page still arrives once, through the new tracker, which
		/// watches the same order, SKU or payment token. The new tracker also takes the payment that a status request of the
		/// superseded tracker still in flight reads (<see cref="OrderTracker.Successor"/>).
		/// <para/>
		/// The superseded request is answered last, once the new tracker is registered, so a failure handler that starts the
		/// purchase again supersedes the new tracker instead of finding its key taken.
		/// </summary>
		private static void StartTracker(OrderTracker tracker)
		{
			var trackingData = tracker.TrackingData;

			OrderTracker supersededTracker = null;
			var isSupersededStopDue = false;
			lock (Sync)
			{
				if (trackingData.orderId != -1)
					Trackers.TryGetValue(trackingData.orderId, out supersededTracker);
				else if (trackingData.sku != null)
					TrackersBySku.TryGetValue(trackingData.sku, out supersededTracker);
				else if (trackingData.token != null)
					TrackersByToken.TryGetValue(trackingData.token, out supersededTracker);

				if (supersededTracker != null)
				{
					Unregister(supersededTracker, out isSupersededStopDue);
					if (trackingData.orderId != -1 || trackingData.sku == null)
						supersededTracker.Successor = tracker;
				}

				if (trackingData.orderId != -1)
					Trackers.Add(trackingData.orderId, tracker);
				else if (trackingData.sku != null)
					TrackersBySku.Add(trackingData.sku, tracker);
				else if (trackingData.token != null)
					TrackersByToken.Add(trackingData.token, tracker);
				tracker.IsRemoved = false;
				tracker.IsStarted = false;
			}

			if (supersededTracker != null && isSupersededStopDue)
				supersededTracker.Stop();

			XDebug.Log(trackingData.settings, $"Order tracker started: {tracker.GetType().Name}");
			tracker.Start();

			bool isRemovedWhileStarting;
			lock (Sync)
			{
				tracker.IsStarted = true;
				isRemovedWhileStarting = tracker.IsRemoved;
			}

			if (isRemovedWhileStarting)
				tracker.Stop();

			if (supersededTracker != null)
			{
				XDebug.LogWarning(trackingData.settings, $"Order tracker superseded by a newer purchase under the same key, its request fails with {ErrorType.Aborted}");
				supersededTracker.TrackingData.errorCallback?.Invoke(new Error(ErrorType.Aborted, errorMessage: "Order tracking stopped: a newer purchase of the same item or payment token replaced it."));
			}
		}
	}
}