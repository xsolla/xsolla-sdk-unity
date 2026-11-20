using System;
using System.Collections.Generic;
using Xsolla.Core;

namespace Xsolla.GetUpdates
{
    internal static class XsollaGetUpdates
    {
        private const string BaseUrl = "https://getupdate.xsolla.com";
        
        public static Dictionary<string, PaymentEvent> eventsCache = new Dictionary<string, PaymentEvent>();
        public static Dictionary<string, EventCache> transactionIdCache = new Dictionary<string, EventCache>();
        public static Dictionary<string, PaymentEvent> invoiceIdCache = new Dictionary<string, PaymentEvent>();

        public static void GetPaymentEvents(XsollaSettings settings, Action<PaymentEvent[]> onSuccess, Action<Error> onError, bool markProcessed)
        {
            _GetPaymentEvents(
                settings,
                onSuccess: evts =>
                {
                    List<PaymentEvent> newEvents = new List<PaymentEvent>();
                    foreach (var evt in evts)
                    {
                        var eventId = $"{settings.StoreProjectId}-{evt.id}";
                        if (!eventsCache.ContainsKey(eventId))
                        {
                            newEvents.Add(evt);
                            eventsCache[eventId] = evt;
                        }
                    }
                    onSuccess?.Invoke(newEvents.ToArray());
                },
                onError: error => onError?.Invoke(error),
                markProcessed: markProcessed
            );
        }

        static void _GetPaymentEvents(XsollaSettings settings, Action<PaymentEvent[]> onSuccess, Action<Error> onError, bool markProcessed)
        {
            GetEvents(
                settings,
                onSuccess: events =>
                {
                    List<PaymentEvent> paymentEvents = new List<PaymentEvent>();
                    
                    // find order_paid and cache it for LAPI + CAPI mix mode
                    foreach (var evt in events)
                    {
                        if (evt.data.notification_type == "order_paid" && (evt.data.settings == null && evt.data.billing?.settings == null))
                        {
                            var paymentEvent = new PaymentEvent(evt);
                            if (!string.IsNullOrEmpty(paymentEvent.transaction_id) && !paymentEvent.isFree)
                                invoiceIdCache[paymentEvent.transaction_id] = paymentEvent;
                        }
                    }

                    foreach (var evt in events)
                    {
                        if (PaymentEvent.IsFreeItem(evt)) // free item, process it always
                        {
                            var paymentEvent = new PaymentEvent(evt);
                            paymentEvents.Add(paymentEvent);
                            transactionIdCache[paymentEvent.transaction_id] = paymentEvent.toEventCache();
                            continue;
                        }
                        
                        if ((evt.data.settings != null && evt.data.settings.project_id != settings.StoreProjectId)
                            || (evt.data.billing?.settings != null && evt.data.billing.settings.project_id != settings.StoreProjectId)
                            || (evt.data.settings == null && evt.data.billing?.settings == null))
                        {
                            if (evt.data.notification_type == "order_paid") // LAPI + CAPI mix mode, will process it with payment event
                                continue;
                            
                            var projectId = evt.data.settings?.project_id ?? evt.data.billing?.settings?.project_id;
                            XDebug.LogWarning(settings, $"Event {evt.id} is not for current project (project_id: {projectId ?? "unknown"}). Skipping.");
                            continue;
                        }

                        if (evt.data.notification_type == "payment" && evt.data.purchase?.order == null) //try to get 
                        {
                            var transaction_id = evt.data.transaction?.id ?? string.Empty;
                            if (invoiceIdCache.TryGetValue(transaction_id, out var invoiceEvent))
                            {
                                var paymentEvent = new PaymentEvent(evt);
                                paymentEvent.order_id = invoiceEvent.order_id;
                                paymentEvent.order_status = invoiceEvent.order_status;
                                paymentEvent.sku = invoiceEvent.sku;
                                paymentEvent.quantity = invoiceEvent.quantity;
                                paymentEvent.priceAmount = invoiceEvent.priceAmount;
                                paymentEvent.priceAmountBeforeDiscount = invoiceEvent.priceAmountBeforeDiscount;
                                paymentEvent.priceCurrency = invoiceEvent.priceCurrency;
                                
                                paymentEvents.Add(paymentEvent);
                                transactionIdCache[paymentEvent.transaction_id] = paymentEvent.toEventCache(invoiceEvent.id);
                            }
                        }
                        else if (evt.data.notification_type == "payment" && evt.data.purchase?.order != null)
                        {
                            var paymentEvent = new PaymentEvent(evt);
                            paymentEvents.Add(paymentEvent);
                            transactionIdCache[paymentEvent.transaction_id] = paymentEvent.toEventCache();
                        }
                        else if (evt.data.notification_type == "order_paid" && evt.data.order != null)
                        {
                            var paymentEvent = new PaymentEvent(evt);
                            paymentEvents.Add(paymentEvent);
                            transactionIdCache[paymentEvent.transaction_id] = paymentEvent.toEventCache();
                        }
                        else if (markProcessed)
                        {
                            EventProcessed(settings, evt.id,
                                () =>
                                {
                                    XDebug.Log(settings, $"Processing event {evt.id} {evt.data.notification_type} completed successfully.");
                                }, 
                                error =>
                                {
                                    XDebug.LogError(settings, $"Error processing event {evt.id} {evt.data.notification_type}: {error}");
                                });
                        }
                    }
                    
                    onSuccess?.Invoke(paymentEvents.ToArray());
                },
                onError: error => onError?.Invoke(error)
            );
        }
        
        /// <summary>
        /// Requests the project's unprocessed events.
        /// <para/>
        /// A rejected token is refreshed and the request repeated once; a repeat rejected again fails instead of
        /// refreshing again, since the Events API can keep rejecting a freshly refreshed token.
        /// </summary>
        public static void GetEvents(XsollaSettings settings, Action<EventItem[]> onSuccess, Action<Error> onError, bool isRetry = false)
        {
            var url = new UrlBuilder($"{BaseUrl}/events")
                .AddProjectIdUnderscore(settings.StoreProjectId)
                .Build();

            WebRequestHelper.Instance.GetRequest<EventItems>(
                SdkType.Store,
                url,
                WebRequestHeader.AuthHeader(settings),
                onComplete: events =>
                {
                    onSuccess?.Invoke(events.events);
                },
                onError: error => {
                    if (isRetry)
                        onError?.Invoke(error);
                    else
                        TokenAutoRefresher.Check(settings, error, onError, () => GetEvents(settings, onSuccess, onError, isRetry: true));
                },
                ErrorGroup.EventsErrors
            );
        }

        /// <summary>
        /// Marks an event processed.
        /// <para/>
        /// A rejected token is refreshed and the request repeated once, as in <see cref="GetEvents"/>.
        /// </summary>
        public static void EventProcessed(XsollaSettings settings, int eventId, Action onSuccess, Action<Error> onError, bool isRetry = false)
        {
            //onError?.Invoke(new Error(errorMessage: "EventProcessed is DISABLED in Standalone SDK."));
            //return;
            
            var url = new UrlBuilder($"{BaseUrl}/events/{eventId}/processed")
                .AddProjectIdUnderscore(settings.StoreProjectId)
                .Build();
            
            WebRequestHelper.Instance.PostRequest(  
                SdkType.Store,
                url,
                WebRequestHeader.AuthHeader(settings),
                onComplete: () =>
                {
                    onSuccess?.Invoke();
                },
                onError: error =>
                {
                    if (isRetry)
                        onError?.Invoke(error);
                    else
                        TokenAutoRefresher.Check(settings, error, onError, () => EventProcessed(settings, eventId, onSuccess, onError, isRetry: true));
                },
                ErrorGroup.EventsErrors);
        }
    }
}