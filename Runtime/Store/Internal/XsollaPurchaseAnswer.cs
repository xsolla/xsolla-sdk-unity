using System.Threading;
using JetBrains.Annotations;
using Xsolla.SDK.Common;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// The answer to one purchase request of a store client whose results can arrive after it has answered the request,
    /// such as the Standalone client, whose order status reply can land after the tracking time limit failed it.
    /// <para/>
    /// The first result answers the request. A success after that is a purchase no request waits for, reported through
    /// the client's channel for purchases made outside a request, as Unity IAP's Google Play store reports a purchase
    /// update while no purchase flow is open. A failure after that is logged and dropped.
    /// <para/>
    /// <see cref="Close"/> ends the answer with the client's session: every result arriving afterwards is logged and
    /// dropped, a success included, so no result of an ended session reaches its callbacks.
    /// <para/>
    /// Thread-safe: the plain .NET SDK delivers order tracking results on background threads.
    /// </summary>
    internal sealed class XsollaPurchaseAnswer
    {
        private const string Tag = "XsollaPurchaseAnswer";

        [CanBeNull]
        private readonly PurchaseProductResultFunc _onSuccess;

        [CanBeNull]
        private readonly ErrorFunc _onError;

        [CanBeNull]
        private readonly PurchaseProductResultFunc _onPurchaseWithoutRequest;

        private const int Open = 0;

        private const int Answered = 1;

        private const int Closed = 2;

        /// <summary>
        /// <see cref="Open"/> until the first result or <see cref="Close"/> answers the request, then
        /// <see cref="Answered"/>, or <see cref="Closed"/> once its session ended.
        /// </summary>
        private int _state;

        private XsollaPurchaseAnswer(
            [CanBeNull] PurchaseProductResultFunc onSuccess,
            [CanBeNull] ErrorFunc onError,
            [CanBeNull] PurchaseProductResultFunc onPurchaseWithoutRequest
        )
        {
            _onSuccess = onSuccess;
            _onError = onError;
            _onPurchaseWithoutRequest = onPurchaseWithoutRequest;
        }

        /// <summary>
        /// Creates the answer to a request answered through <paramref name="onSuccess"/> or <paramref name="onError"/>,
        /// with <paramref name="onPurchaseWithoutRequest"/> taking a success that arrives after the answer.
        /// </summary>
        public static XsollaPurchaseAnswer Of(
            [CanBeNull] PurchaseProductResultFunc onSuccess,
            [CanBeNull] ErrorFunc onError,
            [CanBeNull] PurchaseProductResultFunc onPurchaseWithoutRequest
        ) =>
            new XsollaPurchaseAnswer(onSuccess, onError, onPurchaseWithoutRequest);

        /// <summary>
        /// Answers the request with <paramref name="product"/>, reports it as a purchase without a request when the
        /// request is already answered, or drops it once the answer is closed.
        /// </summary>
        public void Succeed(XsollaStoreClientPurchasedProduct product)
        {
            var previous = Interlocked.CompareExchange(ref _state, Answered, Open);
            if (previous == Open)
            {
                _onSuccess?.Invoke(product);
            }
            else if (previous == Answered)
            {
                XsollaLogger.Warning(Tag, $"Purchase reported after its request was answered, delivered as a purchase without a request: sku={product.sku} transactionId={product.transactionId}");
                _onPurchaseWithoutRequest?.Invoke(product);
            }
            else
            {
                XsollaLogger.Warning(Tag, $"Purchase reported after its session ended, dropped (a paid purchase arrives with the next session's restore): sku={product.sku} transactionId={product.transactionId}");
            }
        }

        /// <summary>
        /// Answers the request with <paramref name="error"/>, or drops it when the request is already answered.
        /// </summary>
        public void Fail(string error)
        {
            if (Interlocked.CompareExchange(ref _state, Answered, Open) == Open)
                _onError?.Invoke(error);
            else
                XsollaLogger.Warning(Tag, $"Purchase failure reported after its request was answered, dropped: {error}");
        }

        /// <summary>
        /// Ends the answer with its session: answers the request with <paramref name="error"/> if nothing answered it yet,
        /// and drops every result arriving afterwards.
        /// <para/>
        /// A success already past its check when the answer closes still reports its purchase once, as the session's last
        /// result.
        /// </summary>
        public void Close(string error)
        {
            if (Interlocked.Exchange(ref _state, Closed) == Open)
                _onError?.Invoke(error);
        }
    }
}
