using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Xsolla.GetUpdates;

namespace Xsolla.Core
{
    internal static class OrderTrackerByEventsUpdater
    {
        /// <summary>
        /// Fetches the payment events this process has not seen yet, without marking them processed.
        /// <para/>
        /// Production keeps <see cref="XsollaGetUpdates.GetPaymentEvents"/>. Tests swap it to script events responses
        /// without a backend.
        /// </summary>
        internal static Action<XsollaSettings, Action<PaymentEvent[]>, Action<Error>> PaymentEventsRequester =
            (settings, onSuccess, onError) => XsollaGetUpdates.GetPaymentEvents(settings, onSuccess, onError, markProcessed: false);

        /// <summary>
        /// How long an updater tracks each tracker from its start before it fails that tracker with
        /// <see cref="ErrorType.TimeLimitReached"/>, unless the endless mode is on. A purchase that starts later does not
        /// extend the limit of one already tracked (<see cref="OrderTrackerByEvents.TimeLimitTime"/>).
        /// <para/>
        /// Production keeps <see cref="Constants.EVENTS_POLLING_LIMIT"/>. Tests shorten it to reach the limit without
        /// waiting it out.
        /// </summary>
        internal static float TimeLimitSeconds = Constants.EVENTS_POLLING_LIMIT;

        /// <summary>
        /// The events updater of each project, guarded by <see cref="OrderTrackingService.Sync"/>: trackers of one project
        /// start and stop on several threads in the plain .NET SDK, and must all find its one updater.
        /// </summary>
        private static readonly Dictionary<string, OrderTrackerByEventsUpdaterInstance> Updaters 
            = new Dictionary<string, OrderTrackerByEventsUpdaterInstance>();
        
        private static OrderTrackerByEventsUpdaterInstance GetUpdater(XsollaSettings settings)
        {
            lock (OrderTrackingService.Sync)
                return Updaters.TryGetValue(settings.StoreProjectId, out var updater) ? updater : null;
        }
        
        private static OrderTrackerByEventsUpdaterInstance GetOrCreateUpdater(XsollaSettings settings)
        {
            lock (OrderTrackingService.Sync)
            {
                if (!Updaters.TryGetValue(settings.StoreProjectId, out var updater))
                {
                    updater = new OrderTrackerByEventsUpdaterInstance(settings);
                    Updaters[settings.StoreProjectId] = updater;
                }

                return updater;
            }
        }
        
        public static void StartEndlessUpdater(XsollaSettings settings, int intervalSec, Action<PaymentEvent[]> onEvent)
        {
            var updater = GetOrCreateUpdater(settings);
            updater.StartEndlessUpdater(settings, intervalSec, onEvent);
        }

        public static void StartUpdater(OrderTrackerByEvents tracker)
        {
            var updater = GetOrCreateUpdater(tracker.TrackingData.settings);
            updater.StartUpdater(tracker);
        }

        public static void StopUpdater(OrderTrackerByEvents tracker)
        {
            var updater = GetUpdater(tracker.TrackingData.settings);
            if (updater != null)
                updater.StopUpdater(tracker);
        }

        public static void RemoveAllFromTracking(XsollaSettings settings)
        {
            var updater = GetUpdater(settings);
            if (updater != null)
                updater.RemoveAllFromTracking();
        }

        /// <summary>
        /// Answers <paramref name="trackers"/> with the events of one events response, and passes the events none of them
        /// took to <paramref name="onUnorderedPurchaseEvent"/>, which may be null.
        /// <para/>
        /// A tracker with an order id takes the events of its order, and a payment token tracker whose order or invoice id
        /// is invalid fails with its stored error (<see cref="OrderTrackerByEvents.GetOrderIdError"/>). A tracker with only
        /// a SKU, a web shop purchase, then takes the first event of its SKU that is left, once its baseline response has
        /// arrived (<see cref="OrderTrackerByEvents.HasEventsBaseline"/>). An event answers at most one tracker: the core
        /// keeps only the first delivery of a purchase, so a second tracker answered with it would get no answer at all.
        /// </summary>
        internal static void HandleEvents(IReadOnlyList<OrderTrackerByEvents> trackers, PaymentEvent[] events, Action<PaymentEvent[]> onUnorderedPurchaseEvent)
        {
            var remainingEvents = new List<PaymentEvent>(events);

            foreach (var tracker in trackers)
            {
                if (!string.IsNullOrEmpty(tracker.TrackingData.sku) || !tracker.CanUseTrackedOrder())
                    continue;

                var orderIdError = tracker.GetOrderIdError();
                if (orderIdError != null)
                {
                    tracker.EndWithError(orderIdError);
                    continue;
                }

                foreach (var evt in events)
                {
                    if (evt.order_id != -1 && evt.order_id == tracker.TrackingData.orderId)
                    {
                        tracker.HandleOrderDone(evt);
                        remainingEvents.Remove(evt);
                    }
                }
            }

            foreach (var tracker in trackers)
            {
                if (!string.IsNullOrEmpty(tracker.TrackingData.sku))
                {
                    if (tracker.HasEventsBaseline)
                    {
                        var evt = remainingEvents.Find(candidate => candidate.sku == tracker.TrackingData.sku);
                        if (evt != null)
                        {
                            tracker.HandleOrderDone(evt);
                            remainingEvents.Remove(evt);
                        }
                    }
                    else
                    {
                        tracker.HasEventsBaseline = true;
                    }
                }
            }

            if (remainingEvents.Count > 0)
                onUnorderedPurchaseEvent?.Invoke(remainingEvents.ToArray());
        }

        class OrderTrackerByEventsUpdaterInstance
        {
            /// <summary>
            /// The coroutine of the current polling loop, guarded by <see cref="OrderTrackingService.Sync"/>. Null while
            /// polling is stopped, and between the decision to start a loop and the start of its coroutine.
            /// </summary>
            private Coroutine _checkStatusCoroutine = null;

            /// <summary>
            /// The generation of the current polling loop, guarded by <see cref="OrderTrackingService.Sync"/>: each start
            /// and stop moves it on, and a loop of an earlier generation ends at its next turn without polling.
            /// <para/>
            /// The plain .NET SDK runs each loop on a thread of its own and stops it only at its next turn, so a loop
            /// replaced by a restart would otherwise poll once more next to its successor.
            /// </summary>
            private int _generation = 0;

            /// <summary>
            /// Whether a loop of <see cref="_generation"/> polls, guarded by <see cref="OrderTrackingService.Sync"/>. It is
            /// decided together with the change of <see cref="_trackers"/> that calls for it, so a tracker that joins while
            /// the last one leaves always finds the updater polling.
            /// </summary>
            private bool _isPolling = false;

            /// <summary>
            /// The trackers the updater answers, guarded by <see cref="OrderTrackingService.Sync"/>: they join and leave on
            /// the threads that start and end purchases, while events responses and the time limits read them on others.
            /// The updater polls at the short interval while any is within its time limit.
            /// </summary>
            private readonly List<OrderTrackerByEvents> _trackers = new List<OrderTrackerByEvents>();

            private bool _isCheckInProgress = false;

            /// If 0, then the endless mode is disabled.
            private float _endlessModeIntervalSecs = 0.0f;

            private float _waitIntervalSecs = Constants.EVENTS_POLLING_INTERVAL;
            private Action<PaymentEvent[]> _onUnorderedPurchaseEvent = null;

            private bool isEndlessEnabled => _endlessModeIntervalSecs > 0.0f;

            /// <summary>
            /// Whether any tracker is within its time limit, under <see cref="OrderTrackingService.Sync"/>. Only the endless
            /// mode keeps trackers past it, and listens for them at its own interval.
            /// </summary>
            private bool isShortPollingActive => _trackers.Exists(tracker => Time.realtimeSinceStartup <= tracker.TimeLimitTime);

            /// <summary>
            /// The settings of the latest session that started a tracker or the endless mode, guarded by
            /// <see cref="OrderTrackingService.Sync"/>.
            /// <para/>
            /// The updater of a project outlives the session that created it, and the SDK initialized again brings new
            /// settings with a new user token. The polling loop reads them on threads of its own in the plain .NET SDK.
            /// </summary>
            private XsollaSettings settings;

            public OrderTrackerByEventsUpdaterInstance(XsollaSettings settings)
            {
                this.settings = settings;
            }

            public void StartEndlessUpdater(XsollaSettings sessionSettings, int intervalSec, Action<PaymentEvent[]> onEvent)
            {
                bool isEnabled;
                var change = PollingChange.None;
                lock (OrderTrackingService.Sync)
                {
                    settings = sessionSettings;
                    _onUnorderedPurchaseEvent = onEvent;

                    // Endless update interval shouldn't be shorter than the "accelerated" update interval.
                    _endlessModeIntervalSecs = intervalSec > 0.0f
                        ? Math.Max(intervalSec, Constants.EVENTS_POLLING_INTERVAL)
                        : 0.0f;

                    isEnabled = isEndlessEnabled;
                    if (isEnabled)
                    {
                        if (!isShortPollingActive)
                            ResetWaitInterval();

                        if (!_isPolling)
                            change = RestartPolling();
                    }
                }

                if (!isEnabled)
                {
                    XDebug.LogWarning(
                        sessionSettings,
                        "Unordered purchase events listener has been assigned, but the endless " +
                        $"mode is OFF due to the interval value provided ({intervalSec}). The listener " +
                        "will be invoked only when doing the short-polling."
                    );
                    return;
                }

                XDebug.Log(sessionSettings, $"[OrderTrackerByEvents] Starting endless updater (interval={intervalSec}s)");

                Apply(change);
            }

            public void StartUpdater(OrderTrackerByEvents tracker)
            {
                bool isAdded;
                var needUpdaterRestart = false;
                var change = PollingChange.None;
                lock (OrderTrackingService.Sync)
                {
                    settings = tracker.TrackingData.settings;
                    isAdded = !_trackers.Contains(tracker);
                    if (isAdded)
                    {
                        needUpdaterRestart = !isShortPollingActive || !_isPolling;

                        tracker.TimeLimitTime = Time.realtimeSinceStartup + TimeLimitSeconds;
                        _trackers.Add(tracker);

                        ResetWaitInterval();

                        if (needUpdaterRestart)
                            change = RestartPolling();
                    }
                }

                if (isAdded)
                {
                    XDebug.Log(tracker.TrackingData.settings, $"[OrderTrackerByEvents] Added a tracker (need_restart={needUpdaterRestart})");

                    Apply(change);
                }
            }

            public void StopUpdater(OrderTrackerByEvents tracker)
            {
                var change = PollingChange.None;
                lock (OrderTrackingService.Sync)
                {
                    var wasTracked = _trackers.Remove(tracker);
                    if (_trackers.Count == 0)
                        change = EndShortPolling(wasTracked);
                }

                Apply(change);
            }

            public void RemoveAllFromTracking()
            {
                OrderTrackerByEvents[] trackers;
                var change = PollingChange.None;
                lock (OrderTrackingService.Sync)
                {
                    trackers = _trackers.ToArray();
                    _trackers.Clear();

                    if (trackers.Length > 0)
                        change = EndShortPolling(wasShortPollingActive: true);
                }

                Apply(change);

                foreach (var tracker in trackers)
                    tracker.ForceStop();
            }

            /// <summary>
            /// Fetches events at the current interval, and fails each tracker past its own time limit with
            /// <see cref="ErrorType.TimeLimitReached"/>. Once none is left it stops.
            /// <para/>
            /// The endless mode keeps the trackers past their limits instead, answers each whenever its event arrives, and
            /// polls at the endless interval once none is within its limit. The limits apply by the mode at each turn, so
            /// trackers kept past them fail at the first turn after the endless mode is turned off.
            /// </summary>
            private IEnumerator TrackEvents(int generation)
            {
                while (true)
                {
                    float waitIntervalSecs;
                    lock (OrderTrackingService.Sync)
                        waitIntervalSecs = _waitIntervalSecs;

                    yield return new WaitForSecondsRealtime(waitIntervalSecs);

                    bool isCurrent;
                    lock (OrderTrackingService.Sync)
                        isCurrent = generation == _generation;

                    if (!isCurrent)
                        yield break;

                    UpdateEvents();

                    var timedOut = new List<OrderTrackerByEvents>();
                    var remainingCount = 0;
                    XsollaSettings logSettings;
                    lock (OrderTrackingService.Sync)
                    {
                        logSettings = settings;

                        // A loop replaced since the check above must not fail the trackers of its successor.
                        if (generation == _generation)
                        {
                            if (!isEndlessEnabled)
                            {
                                var now = Time.realtimeSinceStartup;
                                Predicate<OrderTrackerByEvents> isTimedOut = tracker => now > tracker.TimeLimitTime;
                                timedOut = _trackers.FindAll(isTimedOut);
                                _trackers.RemoveAll(isTimedOut);
                            }

                            remainingCount = _trackers.Count;

                            // In endless mode the last tracker to pass its limit slows the polling to the endless interval.
                            ResetWaitInterval();

                            // Stopped before the answers, so a failure handler that starts a purchase again starts a new
                            // loop instead of finding this one, which ends here.
                            if (timedOut.Count > 0 && remainingCount == 0)
                            {
                                _isPolling = false;
                                _checkStatusCoroutine = null;
                            }
                        }
                    }

                    if (timedOut.Count > 0)
                    {
                        XDebug.Log(logSettings, $"[OrderTrackerByEvents] Time limit reached: failing {timedOut.Count} purchase(s) with TimeLimitReached, {remainingCount} still tracked");

                        var timeLimitError = new Error(ErrorType.TimeLimitReached, errorMessage: "Polling time limit reached");

                        if (remainingCount > 0)
                        {
                            HandleErrors(timedOut, timeLimitError);
                        }
                        else
                        {
                            XDebug.Log(logSettings, "[OrderTrackerByEvents] Completely stopped after short-polling (no endless mode enabled)");
                            HandleErrors(timedOut, timeLimitError);
                            yield break;
                        }
                    }
                }
            }

            private void UpdateEvents()
            {
                XsollaSettings currentSettings;
                lock (OrderTrackingService.Sync)
                    currentSettings = settings;

                if (!currentSettings.XsollaToken.Exists)
                {
                    XDebug.LogWarning(currentSettings, "No Token in order status check. Check cancelled");
                    return;
                }

                lock (OrderTrackingService.Sync)
                {
                    if (_isCheckInProgress) // Prevent double check
                        return;

                    _isCheckInProgress = true;
                }

                PaymentEventsRequester(
                    currentSettings,
                    events =>
                    {
                        OrderTrackerByEvents[] trackers;
                        Action<PaymentEvent[]> onUnorderedPurchaseEvent;
                        lock (OrderTrackingService.Sync)
                        {
                            _isCheckInProgress = false;
                            trackers = _trackers.ToArray();
                            onUnorderedPurchaseEvent = _onUnorderedPurchaseEvent;
                        }

                        HandleEvents(trackers, events, onUnorderedPurchaseEvent);
                    },
                    error =>
                    {
                        lock (OrderTrackingService.Sync)
                            _isCheckInProgress = false;
                    }
                );
            }

            private static void HandleErrors(IReadOnlyList<OrderTrackerByEvents> trackers, Error error)
            {
                foreach (var tracker in trackers)
                    tracker.EndWithError(error);
            }

            /// <summary>
            /// Ends the short polling once no tracker is left, under <see cref="OrderTrackingService.Sync"/>: the updater
            /// goes on at the endless interval if the endless mode is on, or stops otherwise.
            /// </summary>
            /// <param name="wasShortPollingActive">Whether the last tracker left just now. Otherwise the endless mode
            /// already polls at its own interval.</param>
            private PollingChange EndShortPolling(bool wasShortPollingActive)
            {
                ResetWaitInterval();

                if (isEndlessEnabled)
                    return wasShortPollingActive ? RestartPolling() : PollingChange.None;

                return StopPolling();
            }

            private void ResetWaitInterval()
            {
                _waitIntervalSecs = isShortPollingActive || !isEndlessEnabled
                    ? Constants.EVENTS_POLLING_INTERVAL
                    : _endlessModeIntervalSecs;
            }

            /// <summary>
            /// Decides, under <see cref="OrderTrackingService.Sync"/>, that a new loop polls in place of the current one,
            /// which picks up the current interval at once.
            /// </summary>
            private PollingChange RestartPolling()
            {
                var staleCoroutine = _checkStatusCoroutine;
                _checkStatusCoroutine = null;
                _isPolling = true;
                return new PollingChange(staleCoroutine, ++_generation);
            }

            /// <summary>
            /// Decides, under <see cref="OrderTrackingService.Sync"/>, that no loop polls.
            /// </summary>
            private PollingChange StopPolling()
            {
                var staleCoroutine = _checkStatusCoroutine;
                _checkStatusCoroutine = null;
                _isPolling = false;
                _generation++;
                return new PollingChange(staleCoroutine, 0);
            }

            /// <summary>
            /// Carries out a decision of <see cref="RestartPolling"/> or <see cref="StopPolling"/> outside
            /// <see cref="OrderTrackingService.Sync"/>. A loop started for a generation that a later decision has already
            /// replaced is stopped again: that decision found no coroutine of it to stop.
            /// </summary>
            private void Apply(PollingChange change)
            {
                if (change.StaleCoroutine != null)
                {
                    CoroutinesExecutor.Stop(change.StaleCoroutine);

                    XDebug.Log("[OrderTrackerByEvents] Killed updater");
                }

                if (change.Generation == 0)
                    return;

                var coroutine = CoroutinesExecutor.Run(TrackEvents(change.Generation));

                bool isCurrent;
                float waitIntervalSecs;
                XsollaSettings logSettings;
                lock (OrderTrackingService.Sync)
                {
                    isCurrent = change.Generation == _generation;
                    if (isCurrent)
                        _checkStatusCoroutine = coroutine;

                    waitIntervalSecs = _waitIntervalSecs;
                    logSettings = settings;
                }

                if (isCurrent)
                {
                    XDebug.Log(logSettings, "[OrderTrackerByEvents] Started updater (" +
                                       $"interval={waitIntervalSecs}s " +
                                       $"generation={change.Generation}" +
                                       ")"
                    );
                }
                else if (coroutine != null)
                {
                    CoroutinesExecutor.Stop(coroutine);
                }
            }

            /// <summary>
            /// A start or stop of polling decided under <see cref="OrderTrackingService.Sync"/>, to carry out outside it.
            /// </summary>
            private readonly struct PollingChange
            {
                public static readonly PollingChange None = new PollingChange(null, 0);

                /// <summary>The coroutine of the replaced loop, to stop, or null.</summary>
                public readonly Coroutine StaleCoroutine;

                /// <summary>The generation of the loop to start, or 0 to start none.</summary>
                public readonly int Generation;

                public PollingChange(Coroutine staleCoroutine, int generation)
                {
                    StaleCoroutine = staleCoroutine;
                    Generation = generation;
                }
            }
        }
    }


    internal class OrderTrackerByEvents : OrderTracker
    {
        private OrderTrackerByPaystationCallbacksHelper _webHelper = null;

        public OrderTrackerByEvents(OrderTrackingData trackingData) : base(trackingData)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                _webHelper = new OrderTrackerByPaystationCallbacksHelper();
        }

        /// <summary>
        /// Whether an events response has arrived since this tracker started. A tracker with only a SKU takes events from
        /// later responses only.
        /// <para/>
        /// A SKU names no purchase, and the events API returns every event not yet marked processed. The updater receives
        /// only events this process has not seen (<see cref="XsollaGetUpdates.GetPaymentEvents"/>), but the first response
        /// after the start can still hold an older unconsumed purchase of the SKU, for example one from an earlier session
        /// when no restore ran first. Nobody pays in a web shop page within one polling interval of its opening, so that
        /// response holds only purchases made before this one.
        /// </summary>
        public bool HasEventsBaseline { get; set; }

        /// <summary>
        /// The <see cref="Time.realtimeSinceStartup"/> past which the updater fails this tracker with
        /// <see cref="ErrorType.TimeLimitReached"/> unless the endless mode is on, set when the updater adds it. Guarded by
        /// <see cref="OrderTrackingService.Sync"/>.
        /// </summary>
        public float TimeLimitTime { get; set; }

        public override void Start()
        {
            _webHelper?.Start(
                onOk: () => { /* do nothing */ }, 
                onCancel: () => {
                    EndWithError(new Error(ErrorType.UserCancelled));
                }
            );
            
            OrderTrackerByEventsUpdater.StartUpdater(this);
        }

        public override void Stop()
        {
            _webHelper?.Stop();
            
            OrderTrackerByEventsUpdater.StopUpdater(this);
        }

        public void ForceStop()
        {
            RemoveSelfFromTracking();
        }

        public void HandleOrderDone(PaymentEvent evt)
        {
            EndWithSuccess(evt.ToOrderStatus());
        }

        public bool CanUseTrackedOrder() => CheckOrderIdExists();

        /// <summary>
        /// The stored error of a paid payment token whose order or invoice id is invalid, or null.
        /// <para/>
        /// No event names such an order, so the tracker answers with this error, as a polling tracker does
        /// (<see cref="OrderTracker.CheckOrderStatus"/>), instead of waiting out the time limit.
        /// </summary>
        public Error GetOrderIdError()
        {
            lock (OrderTrackingService.Sync)
                return TrackingData.OrderId == 0 ? TrackingData.Error : null;
        }
    }
}
