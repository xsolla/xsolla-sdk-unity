using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Xsolla.SDK.Common;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// Purchase requests waiting for their result from a native client that reports every purchase result through one
    /// shared listener, without saying which request it answers.
    /// <para/>
    /// Each result answers at most one request: the oldest one the result names, else the oldest one. A success names
    /// the SKU it reports. A failure names the request paying with the payment token its message carries as
    /// <c>token=&lt;token&gt;</c>, which the Android SDK puts in every failure of a token flow instead of a SKU, ahead
    /// of the SKUs its message carries as <c>sku=&lt;id&gt;</c>, which the Android SDK appends to every failure of a
    /// product billing flow and the bridge to the failures it raises itself. A failure naming a payment token or SKUs no
    /// request has answers the oldest request, as every waiting request took every result before results were matched,
    /// so a failure naming its purchase differently than the request still answers one. A success of several purchases
    /// is a bundle reported as its contents, which never names the bundle, so it skips the requests of its content SKUs
    /// instead. A success of one SKU no request has answers the oldest request, which may be a bundle of that one SKU.
    /// With one request waiting, that request takes every result.
    /// <para/>
    /// A request queued by <see cref="AddWithoutResult"/> takes no result but the failure of its purchase to start: no rule
    /// above matches it, "oldest" means the oldest request taking results, and with only such requests waiting any other
    /// result answers none. Its purchase reports no result through the listener, as an Android web shop purchase does, so
    /// a success or a failure naming a payment token or no SKU arriving while it waits belongs to another purchase or to
    /// none. Only a failure naming no payment token but its SKU, which the bridge raises when it cannot start the purchase,
    /// reaches it: the newest request of that SKU takes such a failure, whichever kind it is, as a start failure arrives
    /// right after its launch, and an older purchase of the SKU, reporting no result, is presumably abandoned. Its caller
    /// takes it back with <see cref="Remove"/> when the purchase fails to start by throwing.
    /// <para/>
    /// The queue lives as long as its session: <see cref="Close"/> ends it, answering each waiting request once with the
    /// error ending the session, and every request and result arriving afterwards is answered or dropped at once.
    /// <para/>
    /// Thread-safe: requests are added on the start thread and results arrive on the platform's UI thread.
    /// </summary>
    internal sealed class XsollaPurchaseRequestQueue
    {
        /// <summary>
        /// A waiting purchase request, the handle <see cref="Remove"/> takes.
        /// </summary>
        public sealed class Request
        {
            public readonly string sku;

            public readonly Action<string, string> answer;

            /// <summary>
            /// The payment token a token-flow purchase pays with, which the Android SDK names in every failure of that flow
            /// instead of a SKU. Null for every other request.
            /// </summary>
            [CanBeNull] public readonly string paymentToken;

            /// <summary>
            /// Whether a result may answer the request. False for a request whose purchase reports no result through the
            /// listener, which only the failure of its purchase to start answers: through the listener naming its SKU, or
            /// through its caller, after taking it back with <see cref="Remove"/>.
            /// </summary>
            public readonly bool takesResults;

            public Request(
                string sku,
                Action<string, string> answer,
                [CanBeNull] string paymentToken,
                bool takesResults
            )
            {
                this.sku = sku;
                this.answer = answer;
                this.paymentToken = paymentToken;
                this.takesResults = takesResults;
            }
        }

        private const string Tag = "XsollaPurchaseRequestQueue";

        /// <summary>
        /// A character an Xsolla SKU or payment token may contain, so one following a named SKU or token continues it.
        /// </summary>
        private const string IdChar = @"[A-Za-z0-9_.\-]";

        /// <summary>
        /// A SKU a message names, in the characters an Xsolla SKU may contain.
        /// </summary>
        private static readonly Regex NamedSku = new Regex($"sku=({IdChar}+)", RegexOptions.CultureInvariant);

        /// <summary>
        /// The key a message names a payment token with, which a key ending in it, such as <c>access_token=</c>, is not.
        /// </summary>
        private const string TokenKey = "(?<!" + IdChar + ")token=";

        /// <summary>
        /// Any payment token a message names, which only a failure of a token flow carries.
        /// </summary>
        private static readonly Regex NamedToken = new Regex(TokenKey, RegexOptions.CultureInvariant);

        private readonly List<Request> _requests = new List<Request>();

        /// <summary>
        /// The error <see cref="Close"/> ended the queue with, answering every request added afterwards, or null while the
        /// queue is open. Guarded by the lock on <see cref="_requests"/>.
        /// </summary>
        [CanBeNull] private string _closingError;

        /// <summary>
        /// Queues a request of <paramref name="sku"/> that <paramref name="answer"/> answers with (result, error).
        /// </summary>
        public Request Add(string sku, Action<string, string> answer) =>
            Enqueue(new Request(sku, answer, paymentToken: null, takesResults: true));

        /// <summary>
        /// Queues a request of <paramref name="sku"/> paying with <paramref name="paymentToken"/>, if it is not empty, that
        /// <paramref name="answer"/> answers with (result, error). A failure naming the token as
        /// <c>token=&lt;paymentToken&gt;</c> answers this request.
        /// </summary>
        public Request Add(string sku, [CanBeNull] string paymentToken, Action<string, string> answer) =>
            Enqueue(new Request(
                sku,
                answer,
                paymentToken: string.IsNullOrEmpty(paymentToken) ? null : paymentToken,
                takesResults: true
            ));

        /// <summary>
        /// Queues a request of <paramref name="sku"/> that no result answers, for a purchase whose result the listener
        /// never reports, such as an Android web shop purchase. It waits only for the purchase failing to start: a failure
        /// naming no payment token but <paramref name="sku"/> answers the newest such request of it, and its caller takes
        /// it back with <see cref="Remove"/> and answers it through <paramref name="answer"/> when the start throws.
        /// </summary>
        public Request AddWithoutResult(string sku, Action<string, string> answer) =>
            Enqueue(new Request(sku, answer, paymentToken: null, takesResults: false));

        /// <summary>
        /// Appends <paramref name="request"/> as the newest waiting request, or, once the queue is closed, answers it at once
        /// with the error that closed it, outside the lock: its session has ended, so no result of it will be taken.
        /// </summary>
        private Request Enqueue(Request request)
        {
            string closingError;
            lock (_requests)
            {
                closingError = _closingError;
                if (closingError == null)
                    _requests.Add(request);
            }

            if (closingError != null)
            {
                XsollaLogger.Warning(Tag, $"Purchase request of sku={request.sku} added after its session ended, answered with the error that ended it");
                request.answer(null, closingError);
            }
            return request;
        }

        /// <summary>
        /// Ends the queue with its session: answers each request taking results with <paramref name="error"/> once,
        /// outside the lock, and drops each request taking no result unanswered. Every result arriving afterwards answers
        /// none, and <see cref="TryAnswer"/> drops it; every request added afterwards is answered with
        /// <paramref name="error"/> at once. A second call answers nothing and keeps the first error.
        /// <para/>
        /// A request taking no result waits only for its purchase to fail to start, which happens right after it is added,
        /// while one that started has no result to wait for: an Android web shop purchase that was paid arrives with the
        /// next restore. Answering it would fail every web shop purchase the session started, paid ones included.
        /// <para/>
        /// An answer throwing is logged and does not keep the requests after it from being answered.
        /// </summary>
        public void Close(string error)
        {
            List<Request> answered;
            int dropped;
            lock (_requests)
            {
                if (_closingError == null)
                    _closingError = error;
                answered = _requests.FindAll(r => r.takesResults);
                dropped = _requests.Count - answered.Count;
                _requests.Clear();
            }

            XsollaLogger.Debug(Tag, $"Closed: {answered.Count} waiting requests answered with the closing error, {dropped} taking no result dropped");
            foreach (var request in answered)
            {
                try
                {
                    request.answer(null, error);
                }
                catch (Exception e)
                {
                    XsollaLogger.Error(Tag, $"Answer of the purchase request of sku={request.sku} threw while closing: {e}");
                }
            }
        }

        /// <summary>
        /// Removes <paramref name="request"/> if no result has answered it yet.
        /// </summary>
        /// <returns>True when it was still waiting and is removed.</returns>
        public bool Remove(Request request)
        {
            lock (_requests)
            {
                return _requests.Remove(request);
            }
        }

        /// <summary>
        /// Answers the request <paramref name="result"/> or <paramref name="error"/> belongs to, outside the lock.
        /// </summary>
        /// <returns>
        /// False when no request is waiting, or the result names no request it may answer while no request taking results
        /// waits, so it answers none. True once the queue is closed: the result belongs to a session that has ended, so it
        /// is dropped instead of reaching the purchases without a request.
        /// </returns>
        public bool TryAnswer([CanBeNull] string result, [CanBeNull] string error)
        {
            Request request;
            int waiting;
            bool isNamed;
            bool isClosed;
            lock (_requests)
            {
                // A closed queue holds no request, so the result answers none.
                isClosed = _closingError != null;
                waiting = _requests.Count;
                var named = waiting == 0 ? -1 : IndexOfNamedRequest(result, error);
                isNamed = named >= 0;
                var index = isNamed ? named : _requests.FindIndex(r => r.takesResults);
                if (index < 0)
                {
                    request = null;
                }
                else
                {
                    request = _requests[index];
                    _requests.RemoveAt(index);
                }
            }

            if (request != null)
            {
                XsollaLogger.Debug(Tag, $"Purchase result answers the request of sku={request.sku} ({(isNamed ? "named" : "oldest")}, {waiting} waiting)");
                request.answer(result, error);
                return true;
            }
            else if (isClosed)
            {
                XsollaLogger.Warning(Tag, $"Purchase {(result != null ? "success" : $"failure {error}")} arrived after its session ended, dropped (a paid purchase arrives with the next restore)");
                return true;
            }
            else
            {
                if (waiting > 0)
                    XsollaLogger.Debug(Tag, $"Purchase result names no waiting request it may answer and no request taking results waits, so it answers none ({waiting} waiting)");
                return false;
            }
        }

        /// <summary>
        /// The index of the request taking results that a result names, or -1 when it names none, so the oldest request
        /// taking results takes it. A failure names the request paying with its payment token, else the oldest one of a SKU
        /// it names, as the bridge may append the SKU to a token flow failure. A failure naming no payment token but the SKU
        /// of a request taking no result names the newest request of that SKU instead, whichever kind it is, as a failure
        /// to start a purchase.
        /// <para/>
        /// Requests take no result only in WebShop mode, where the Android bridge starts the token flow for a purchase with
        /// a payment token and the web shop for any other. A token flow failure names its payment token, so only failures
        /// the bridge raises at the start name a SKU alone: the web shop not starting, or an external transaction token
        /// it cannot pass on, which a purchase with a payment token may carry as well.
        /// </summary>
        private int IndexOfNamedRequest([CanBeNull] string result, [CanBeNull] string error)
        {
            if (result != null)
            {
                var skus = ReportedSkus(result);
                if (skus.Length == 1)
                    return _requests.FindIndex(r => r.takesResults && r.sku == skus[0]);
                else if (skus.Length > 1)
                    return _requests.FindIndex(r => r.takesResults && !skus.Contains(r.sku));
                else
                    return -1;
            }
            else
            {
                var message = XsollaStoreClientHelpers.ParsePurchaseError(error).message ?? string.Empty;
                var namesToken = NamedToken.IsMatch(message);
                var paying = namesToken ? IndexOfPayingRequest(message) : -1;
                if (paying >= 0)
                {
                    return paying;
                }
                else
                {
                    var named = NamedSkus(error);
                    return !namesToken && _requests.Exists(r => !r.takesResults && named.Contains(r.sku))
                        ? _requests.FindLastIndex(r => named.Contains(r.sku))
                        : _requests.FindIndex(r => r.takesResults && named.Contains(r.sku));
                }
            }
        }

        /// <summary>
        /// The index of the oldest request taking results whose payment token the failure <paramref name="message"/> names
        /// as <c>token=&lt;token&gt;</c>, or -1. The token is matched literally in the parsed message, as it may hold
        /// characters a pattern or JSON treats specially, and only where it ends: the Android SDK follows it with a space or
        /// a closing parenthesis, and a token it is a prefix of goes on with an <see cref="IdChar"/>.
        /// </summary>
        private int IndexOfPayingRequest(string message) =>
            _requests.FindIndex(r =>
                r.takesResults
                && r.paymentToken != null
                && Regex.IsMatch(message, $"{TokenKey}{Regex.Escape(r.paymentToken)}(?!{IdChar})", RegexOptions.CultureInvariant)
            );

        private static string[] ReportedSkus(string result) =>
            XsollaClientHelpers.FromJson<XsollaStoreClientPurchasedProductResult>(result)?.purchases?
                .Select(p => p?.sku ?? string.Empty)
                .Where(sku => sku.Length > 0)
                .ToArray()
            ?? Array.Empty<string>();

        private static string[] NamedSkus([CanBeNull] string error) =>
            string.IsNullOrEmpty(error)
                ? Array.Empty<string>()
                : NamedSku.Matches(error).Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
    }
}
