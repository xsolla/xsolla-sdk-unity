using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;
using Xsolla.SDK.Common;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// The Unity IAP-neutral part of the Xsolla purchasing store, driven by one Unity IAP binding and reporting back
    /// through <see cref="IXsollaPurchasingCoreCallback"/>.
    /// <para/>
    /// Owns resolving the configuration, initializing the store client, the catalog (fetching it, mapping it to
    /// <see cref="XsollaPurchasingProduct"/> and keeping every fetched product for lookups) and the purchase flow:
    /// starting purchases, receiving every purchase result the store client reports and delivering each purchase at
    /// most once per transaction and SKU (and, with <see cref="XsollaPurchasingCoreOptions.deduplicatesByOrderId"/>,
    /// once per order and SKU among the purchases it delivers through
    /// <see cref="IXsollaPurchasingCoreCallback.OnPurchaseSucceeded"/>), then consuming each one at most once per transaction and SKU. Behaviours only one Unity
    /// IAP line needs are chosen through <see cref="XsollaPurchasingCoreOptions"/>.
    /// <para/>
    /// Restored purchases take the same delivery path. <see cref="FetchPurchases"/>, and <see cref="RestoreTransactions"/>
    /// with <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>, deliver a restore once the catalog
    /// is fetched and answer after the deliveries; with that option, a restored purchase without an identity is
    /// delivered only for the units of its SKU neither handed over yet nor of a purchase waiting for a result its store
    /// client can answer. With <see cref="XsollaPurchasingCoreOptions.restoresNonConsumablesUnderNativeIds"/> instead,
    /// every restore puts a restored purchase without an identity of a SKU the binding typed as a non-consumable or a
    /// subscription back under the id Unity IAP 4.x logged for it: the id of the purchase this device granted it as,
    /// when the record passed to the constructor holds one, else the id the Android SDK made up for it.
    /// <para/>
    /// Not synchronized: the binding calls it on the thread its store client calls back on (the Unity main thread in a
    /// player), and every store client callback arrives there too; the standalone client delivers all web-request
    /// callbacks via coroutine continuations.
    /// </summary>
    internal sealed class XsollaPurchasingCore : IXsollaPurchasingExtensionCore
    {
        /// <summary>
        /// Log tag; the Unity IAP 4.x store logged under it before this logic moved here, and the log messages keep that
        /// store's wording too, so existing log filters keep matching.
        /// </summary>
        private const string Tag = "XsollaPurchasingStore";

        private readonly IXsollaStoreClient _storeClient;

        private readonly IXsollaPurchasingCoreCallback _callback;

        private readonly XsollaPurchasingCoreOptions _options;

        private readonly ISimpleFuture<XsollaClientConfiguration, string> _settingsFuture;

        [CanBeNull] private ISimpleFuture<bool, string> _initializedFuture;

        /// <summary>
        /// Completes once the first <see cref="FetchProducts"/> after <see cref="Initialize"/> finishes: true when it
        /// fetched the catalog, false when it failed, including when the configuration failed or the store client
        /// threw. It completes right after the binding callback that reports that fetch, also when that callback
        /// throws. It never completes with an error, and never completes at all if no such call is made.
        /// <para/>
        /// Purchase delivery waits on it so that a purchase never reaches the binding before the catalog does. Its
        /// completion runs every held delivery, also when one throws.
        /// </summary>
        private readonly ISimpleFuture<bool, string> _productsFuture;

        /// <summary>
        /// Completes <see cref="_productsFuture"/>; held until the first <see cref="FetchProducts"/> after
        /// <see cref="Initialize"/> takes it, so null means that fetch has started.
        /// </summary>
        [CanBeNull] private ISimplePromise<bool, string> _productsPromise;

        private readonly Dictionary<string, XsollaStoreClientProduct> _productById = new Dictionary<string, XsollaStoreClientProduct>();

        /// <summary>
        /// The type of every SKU a <see cref="FetchProducts"/> call was given one for, as the first such call gave it;
        /// read only with <see cref="XsollaPurchasingCoreOptions.restoresNonConsumablesUnderNativeIds"/>.
        /// <para/>
        /// Unity IAP 4.x keeps the product it registered first when a later fetch of additional products registers the
        /// SKU again under another type, so a later call's type would differ from the one Unity IAP grants and
        /// finishes the purchase as.
        /// </summary>
        private readonly Dictionary<string, XsollaPurchasingProductType> _productTypeBySku =
            new Dictionary<string, XsollaPurchasingProductType>();

        /// <summary>
        /// Purchased quantity per (transaction, sku): what <see cref="Consume"/> drains, dropped once it consumed the
        /// purchase, or at once when the purchase needs no consume.
        /// <para/>
        /// Each report of a purchase not yet handed to the binding records its quantity, and the hand-over records the
        /// quantity of the purchase handed over (delivered through
        /// <see cref="IXsollaPurchasingCoreCallback.OnPurchaseSucceeded"/>, or as
        /// <see cref="XsollaPurchasingProduct.restoredPurchase"/> in the first <see cref="FetchProducts"/>). That one
        /// then stands, since later reports of a handed-over purchase are dropped: a consume drains the quantity the
        /// binding was given, even when a later report of the same purchase arrived while its delivery waited for the
        /// catalog, and a consume before any hand-over (of a dropped purchase, or of one the binding only knows from
        /// <see cref="IXsollaPurchasingCoreCallback.OnPurchasesFetched"/>) drains the latest report. Nothing is
        /// recorded for a purchase whose consume already succeeded, since <see cref="Consume"/> never reads it again,
        /// nor for one without a transaction id, nor for a report dropped as a duplicate of an order delivered under
        /// another transaction id: its consume drains 1.
        /// <para/>
        /// A consume request carries only the transaction id and SKU, but consuming a collapsed multi-unit restore
        /// needs the purchased quantity. Keyed by sku as well, not by transaction id alone: a transaction id is not
        /// guaranteed to own a single sku (bundle and virtual-currency content lines share one), so a transaction-only
        /// key lets the last line overwrite the rest.
        /// </summary>
        private readonly Dictionary<(string transactionId, string sku), int> _quantityByPurchase =
            new Dictionary<(string transactionId, string sku), int>();

        /// <summary>
        /// The result callbacks of every <see cref="Consume"/> request waiting on the consume in flight for a
        /// (transaction, sku): the request that dispatched it, then each repeat that arrived before it called back.
        /// <para/>
        /// A repeat must not consume again: that spends a second inventory unit on Standalone and iOS, and on Android
        /// a purchase token the receipt cache has already spent. A failed consume drops its key, so a later request
        /// can retry.
        /// </summary>
        private readonly Dictionary<(string transactionId, string sku), List<(Action onSuccess, Action<string> onFailed)>>
            _consumeRequestsInFlight =
                new Dictionary<(string transactionId, string sku), List<(Action onSuccess, Action<string> onFailed)>>();

        /// <summary>
        /// Every (transaction, sku) whose consume succeeded this session, so a repeated <see cref="Consume"/>
        /// succeeds at once without consuming again.
        /// <para/>
        /// Never cleared, making the dedup session-scoped by design: the server stays the source of truth across
        /// runs.
        /// </summary>
        private readonly HashSet<(string transactionId, string sku)> _consumedPurchases =
            new HashSet<(string transactionId, string sku)>();

        /// <summary>
        /// Every (transaction, sku) already handed to the binding this session, so a duplicate delivery is dropped.
        /// <para/>
        /// Catches a native re-delivery of an owned, unconsumed item and a restore racing a purchase. A genuine
        /// re-purchase gets a new transaction id, so it is not blocked. Caveat: this also drops a re-delivery that
        /// would have retried a first delivery whose validation or consume failed; that purchase stays pending and
        /// resurfaces next session.
        /// Marked wherever a purchase is handed over: on delivery through
        /// <see cref="IXsollaPurchasingCoreCallback.OnPurchaseSucceeded"/> and as
        /// <see cref="XsollaPurchasingProduct.restoredPurchase"/> in the first <see cref="FetchProducts"/> (only with
        /// <see cref="XsollaPurchasingCoreOptions.restoresWithFirstFetch"/>). Also marked when a report is dropped as a
        /// duplicate of an order delivered under another transaction id.
        /// </summary>
        private readonly HashSet<(string transactionId, string sku)> _deliveredPurchases =
            new HashSet<(string transactionId, string sku)>();

        /// <summary>
        /// Every (order, sku) with an order id already delivered through
        /// <see cref="IXsollaPurchasingCoreCallback.OnPurchaseSucceeded"/> this session, so a report of that order under
        /// another transaction id is dropped; filled only with
        /// <see cref="XsollaPurchasingCoreOptions.deduplicatesByOrderId"/>.
        /// <para/>
        /// Keyed by sku as well: the content lines of one bundle or virtual-currency order are reported separately and
        /// share its order id. Never cleared, like <see cref="_deliveredPurchases"/>.
        /// </summary>
        private readonly HashSet<(long orderId, string sku)> _deliveredOrders = new HashSet<(long orderId, string sku)>();

        /// <summary>
        /// The transaction id each purchase of a non-consumable was granted under, by the id the Android SDK made up
        /// for its holding, which every restore reads to hand the holding over under the id Unity IAP 4.x logged at the
        /// purchase (see <see cref="WithNativeIdsOfNonConsumables"/>). Null without
        /// <see cref="XsollaPurchasingCoreOptions.restoresNonConsumablesUnderNativeIds"/>, or where the store client
        /// makes up no ids.
        /// </summary>
        [CanBeNull] private readonly XsollaNonConsumableTransactionIdsAndroid _nonConsumableTransactionIds;

        /// <summary>
        /// Every (transaction, sku) of a live purchase of a non-consumable handed to the binding this session and not
        /// finished yet, whose holding <see cref="Consume"/> records in <see cref="_nonConsumableTransactionIds"/>,
        /// with <see cref="_accessTokenUpdates"/> as it stood at the delivery; filled only while that record is set.
        /// </summary>
        private readonly Dictionary<(string transactionId, string sku), int> _nonConsumablePurchasesToRecord =
            new Dictionary<(string transactionId, string sku), int>();

        /// <summary>
        /// Quantity of every purchase with a transaction id handed to the binding this session and not consumed since,
        /// by (transaction, sku); filled only with <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>,
        /// wherever <see cref="_deliveredPurchases"/> marks a hand-over: through
        /// <see cref="IXsollaPurchasingCoreCallback.OnPurchaseSucceeded"/> and as
        /// <see cref="XsollaPurchasingProduct.restoredPurchase"/> in the first <see cref="FetchProducts"/>.
        /// <para/>
        /// Summed per SKU, it is how many owned units of the SKU the binding already holds, which a restore's
        /// identity-less purchases of that SKU report again. A consumable's successful consume drops its entry and
        /// counts its units into every open <see cref="RestoreWindow"/> but the one that handed it over; a confirmed
        /// non-consumable keeps it, since the item stays owned and every restore reports it again. The units
        /// belong to the user they were handed to: <see cref="UpdateAccessToken"/> to another user sets them aside
        /// in <see cref="_parkedHandOvers"/> until that user's token is set again.
        /// </summary>
        private readonly Dictionary<(string transactionId, string sku), int> _unconsumedHandOvers =
            new Dictionary<(string transactionId, string sku), int>();

        /// <summary>
        /// Units of the <see cref="Purchase"/> requests not answered yet, by the <see cref="_accessTokenUpdates"/> count
        /// the request went out at (see <see cref="SwitchEpochOf"/>) and requested SKU; filled only with
        /// <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>, one unit per request the store client
        /// can answer (<see cref="IXsollaStoreClient.CanAnswerPurchase"/>), as a request names no quantity. Only the units
        /// of the current user's epochs hold back a restore's purchases.
        /// <para/>
        /// The Standalone inventory credits a unit at payment while the purchase answer still waits for the order
        /// tracker, so a restore in between reports that unit identity-less ahead of the purchase result. A failed
        /// request drops its unit at the answer; a successful one when its delivery is released, just before the
        /// delivery records the hand-over that holds the unit in <see cref="_unconsumedHandOvers"/> from then on.
        /// </summary>
        private readonly Dictionary<(int update, string sku), int> _purchaseUnitsInFlight =
            new Dictionary<(int update, string sku), int>();

        /// <summary>
        /// The restores issued through <see cref="RestoreThenDeliver"/> whose batch is not delivered yet; filled only
        /// with <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>.
        /// </summary>
        private readonly List<RestoreWindow> _restoreWindows = new List<RestoreWindow>();

        /// <summary>
        /// The entries of <see cref="_unconsumedHandOvers"/> set aside while another user's token is set, by the
        /// user they were handed to (see <see cref="UserOf"/>); filled only with
        /// <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>, by <see cref="UpdateAccessToken"/>, and
        /// by a purchase or a <see cref="FetchProducts"/> answered once a switch away from the user it was issued for
        /// is decided.
        /// <para/>
        /// Set aside, they hold nothing back from the other user's restores; once their user's token is set again
        /// they move back, so that user's restores do not hand the units over a second time. A successful consume
        /// drops an entry here as it does there.
        /// </summary>
        private readonly Dictionary<string, Dictionary<(string transactionId, string sku), int>> _parkedHandOvers =
            new Dictionary<string, Dictionary<(string transactionId, string sku), int>>();

        /// <summary>
        /// How many tokens <see cref="UpdateAccessToken"/> handed the store client. A request or a restore keeps the
        /// count it went out at, which tells its switch epoch (see <see cref="SwitchEpochOf"/>), and a purchase of a
        /// non-consumable the count at its delivery, which tells whether the token changed before its holding was read
        /// (see <see cref="_nonConsumablePurchasesToRecord"/>).
        /// </summary>
        private int _accessTokenUpdates;

        /// <summary>
        /// Every switch epoch, the last one current: the <see cref="_accessTokenUpdates"/> count of the update that
        /// started it, and its user. <see cref="UpdateAccessToken"/> starts an epoch with every switch to another
        /// user it decides, which happens only with <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>.
        /// An ended epoch's user is the previous user the switch that ended it compared against; the first epoch
        /// starts at count 0, its user null until then. Switches are decided in the order their updates went out, so
        /// the counts grow along the list.
        /// <para/>
        /// A request or a restore belongs to the epoch of the last update that went out before it and started one, as
        /// the store client serves calls in call order: one sent while a switch is not decided yet is the new user's
        /// once it is, and one sent behind an update that starts none stays the previous user's. A purchase answered
        /// after a switch hands its units over to the user it was requested for, its units waiting for the answer
        /// hold back only that user's restores, and a restore issued before a switch read the previous user's
        /// inventory, so it delivers none of its identity-less purchases.
        /// </summary>
        private readonly List<(int update, string user)> _switchEpochs = new List<(int update, string user)> { (0, null) };

        /// <summary>
        /// The <see cref="UpdateAccessToken"/> calls whose switch is not decided yet, in the order they went out; filled
        /// only with <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>.
        /// </summary>
        private readonly List<AccessTokenUpdate> _undecidedAccessTokenUpdates = new List<AccessTokenUpdate>();

        /// <summary>
        /// The user each token handed to the store client names (see <see cref="UserOf"/>), by the
        /// <see cref="_accessTokenUpdates"/> count it was handed at: the configuration's token, then one per
        /// <see cref="UpdateAccessToken"/>; null where no user can be read or the update failed. Filled only without
        /// <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>, whose switch epochs track the users.
        /// </summary>
        private readonly List<string> _usersByUpdate = new List<string> { null };

        /// <summary>
        /// The <see cref="_accessTokenUpdates"/> count each purchase handed to the binding and not finished yet went out
        /// at: of the request it answers or the restore that reported it, by (transaction, sku); filled only without
        /// <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>.
        /// <para/>
        /// <see cref="Consume"/> reads it to refuse a consumable of another user than the current one: the store clients
        /// consume by SKU under their current token, so the consume would spend a unit of the current user.
        /// </summary>
        private readonly Dictionary<(string transactionId, string sku), int> _updateByHandOver =
            new Dictionary<(string transactionId, string sku), int>();

        /// <summary>
        /// Initializes a new instance of the <see cref="XsollaPurchasingCore"/> class and starts resolving the
        /// configuration: at once, or once its delayed configuration task maps it. A delayed configuration task that
        /// faults or is cancelled, or a mapper that throws or returns null, fails the configuration, which
        /// <see cref="Initialize"/> and <see cref="FetchProducts"/> then report.
        /// </summary>
        /// <param name="configuration">Client configuration, possibly with a delayed configuration task.</param>
        /// <param name="storeClient">Store client the core drives.</param>
        /// <param name="callback">Receives everything the core reports.</param>
        /// <param name="options">The binding's Unity IAP line-specific startup behaviours.</param>
        /// <param name="nonConsumableTransactionIds">
        /// The record of the transaction id each purchase of a non-consumable was granted under, read and written only
        /// with <see cref="XsollaPurchasingCoreOptions.restoresNonConsumablesUnderNativeIds"/>, or null where the store
        /// client makes up no ids.
        /// </param>
        /// <param name="runOnStartThread">
        /// Runs the delayed configuration's mapper on the start thread; null means <see cref="RunOnStartThread.Run"/>.
        /// Tests outside Unity pass one that runs it in place, since <see cref="RunOnStartThread"/> queues work from
        /// any other thread for a Unity <c>Update</c> that never comes there.
        /// </param>
        public XsollaPurchasingCore(
            XsollaClientConfiguration configuration,
            IXsollaStoreClient storeClient,
            IXsollaPurchasingCoreCallback callback,
            XsollaPurchasingCoreOptions options,
            [CanBeNull] XsollaNonConsumableTransactionIdsAndroid nonConsumableTransactionIds = null,
            [CanBeNull] Action<Action> runOnStartThread = null
        )
        {
            _storeClient = storeClient;
            _callback = callback;
            _options = options;
            _nonConsumableTransactionIds = options.restoresNonConsumablesUnderNativeIds
                ? nonConsumableTransactionIds
                : null;
            _productsFuture = SimpleFuture.Create<bool, string>(out _productsPromise);

            RunOnStartThread.Create();

            _settingsFuture = SimpleFuture.Create<XsollaClientConfiguration, string>(out var promise);
            XsollaLogger.Apply(configuration);
            if (configuration.delayedTask != null)
                XsollaDelayedConfiguration.Resolve(configuration, runOnStartThread ?? RunOnStartThread.Run, promise, Tag);
            else
                promise.Complete(configuration);
        }

        /// <summary>
        /// Initializes the store client once the configuration is resolved.
        /// <para/>
        /// Catalog fetches wait for the configuration, not for the client's own result, which is reported through
        /// <see cref="IXsollaPurchasingCoreCallback.OnInitialized"/> or
        /// <see cref="IXsollaPurchasingCoreCallback.OnInitializeFailed"/>.
        /// Purchases the store client reports by itself, outside a purchase request, are delivered like the results of
        /// <see cref="Purchase"/>; failures it reports by itself carry no SKU and are only logged.
        /// <para/>
        /// A store client that throws before answering fails initialization: the core logs the exception, reports
        /// <see cref="IXsollaPurchasingCoreCallback.OnInitializeFailed"/> with its message, and every waiting and later
        /// <see cref="FetchProducts"/> fails with it, as after a failed configuration. The exception then propagates to
        /// the caller when the configuration was already resolved, unless a binding callback reporting the failure
        /// throws, whose exception propagates instead; when this call waited for a delayed configuration, it is only
        /// reported, as the resolution has no caller to reach. A binding callback that throws while reporting the client's
        /// answer or a failure does not hold the fetches back: they go ahead or fail first, and its exception then
        /// propagates unchanged, while a waiting fetch whose report throws meanwhile is only reported.
        /// </summary>
        public void Initialize()
        {
            XsollaLogger.Debug(Tag, "Initialize");

            _initializedFuture = SimpleFuture.Create<bool, string>(out var promise);
            // A resolved configuration runs the callbacks below inside this call; a pending one, from its resolution.
            var isInCall = _settingsFuture.isCompleted;
            _settingsFuture.OnComplete(
                onSuccess: configuration => {
                    XsollaLogger.Apply(configuration);
                    if (!_options.limitsIdentitylessRestores)
                        _usersByUpdate[0] = UserOf(configuration.accessToken);

                    var isAnswered = false;
                    var isFailed = false;
                    var isReturned = false;
                    try
                    {
                        _storeClient.Initialize(
                            configuration,
                            onSuccess: () => {
                                isAnswered = true;
                                XsollaLogger.Debug(Tag, "Initialize finished");
                                _callback.OnInitialized();
                            },
                            onError: error => {
                                isAnswered = true;
                                XsollaLogger.Debug(Tag, $"Initialize failed: {error}");
                                _callback.OnInitializeFailed(error);
                            },
                            onSuccessPurchaseProduct: product => OnPurchaseSucceeded(product, null, _accessTokenUpdates),
                            onErrorPurchase: error => OnPurchaseFailed(error, null)
                        );
                        isReturned = true;
                    }
                    catch (Exception e) when (!isAnswered)
                    {
                        // Nothing else reports this initialization or completes the future the fetches wait on.
                        isFailed = true;
                        XsollaLogger.Error(Tag, $"Initialize failed: the store client threw: {e}");
                        ReportInitializeFailedThenFail(promise, $"initialization failed: {e.Message}", isInCall);

                        if (isInCall)
                            throw;
                    }
                    finally
                    {
                        if (isReturned)
                            promise.Complete(true);
                        // The client answered, then it or the binding callback reporting its answer threw.
                        else if (!isFailed)
                            CompleteWhileThrowing(() => promise.Complete(true), "Initialize: a waiting fetch");
                    }
                },
                onError: error => {
                    XsollaLogger.Error(Tag, $"Initialize failed: {error}");
                    ReportInitializeFailedThenFail(promise, error, false);
                }
            );
        }

        /// <summary>
        /// Reports <paramref name="error"/> through <see cref="IXsollaPurchasingCoreCallback.OnInitializeFailed"/>, then
        /// fails <paramref name="promise"/> with it, also when the binding callback throws, whose exception still
        /// propagates unchanged once the waiting fetches have failed. A fetch report that throws while the binding's
        /// exception propagates, or while <paramref name="isRethrowing"/> (the caller rethrows its own afterwards), is
        /// only reported.
        /// </summary>
        private void ReportInitializeFailedThenFail(ISimplePromise<bool, string> promise, string error, bool isRethrowing)
        {
            var isReported = false;
            try
            {
                _callback.OnInitializeFailed(error);
                isReported = true;
            }
            finally
            {
                if (isReported && !isRethrowing)
                    promise.CompleteWithError(error);
                else
                    CompleteWhileThrowing(() => promise.CompleteWithError(error), "Initialize: a waiting fetch");
            }
        }

        /// <summary>
        /// Fetches the catalog of the given SKUs and reports it, for each call, through
        /// <see cref="IXsollaPurchasingCoreCallback.OnProductsFetched"/> or
        /// <see cref="IXsollaPurchasingCoreCallback.OnProductsFetchFailed"/>.
        /// <para/>
        /// Called before <see cref="Initialize"/>, it fails at once; otherwise it waits for the configuration. The
        /// first call after <see cref="Initialize"/> releases the purchases waiting for the catalog once it finishes,
        /// right after reporting it, even when it fails, when the configuration failed, or when the binding callback
        /// reporting it throws, whose exception still propagates unchanged. When the store client throws before
        /// answering, that call releases them too. A call made once the store client's initialization has returned lets
        /// the client's exception propagate to the caller and leaves the fetch unanswered; a call queued behind
        /// initialization runs from its completion, where no caller would see the exception, so it fails with the
        /// exception's message instead, and the exception is only logged.
        /// With <see cref="XsollaPurchasingCoreOptions.restoresWithFirstFetch"/>, that call also restores the user's
        /// unconsumed purchases (except on iOS) and hands each one over on its product as
        /// <see cref="XsollaPurchasingProduct.restoredPurchase"/>; a failed restore fails that fetch, and no later call
        /// restores again. Without it, no call restores.
        /// </summary>
        /// <param name="skus">SKUs of the products to fetch.</param>
        /// <param name="productTypes">
        /// The type of each SKU, by SKU, which every later restore reads (see
        /// <see cref="XsollaPurchasingCoreOptions.restoresNonConsumablesUnderNativeIds"/>), or null. A SKU an earlier
        /// call gave a type keeps that one.
        /// </param>
        public void FetchProducts(
            IReadOnlyCollection<string> skus,
            [CanBeNull] IReadOnlyDictionary<string, XsollaPurchasingProductType> productTypes = null
        )
        {
            XsollaLogger.Debug(Tag, "RetrieveProducts");

            // Recorded at the call rather than with the catalog, which a restore may answer before.
            if (productTypes != null)
            {
                foreach (var productType in productTypes)
                {
                    if (!_productTypeBySku.ContainsKey(productType.Key))
                        _productTypeBySku.Add(productType.Key, productType.Value);
                }
            }

            if (_initializedFuture == null)
            {
                XsollaLogger.Error(Tag, "RetrieveProducts: not initialized");
                _callback.OnProductsFetchFailed("not initialized");
            }
            else
            {
                // Non-null only for the first call after Initialize, which completes the products future.
                var promise = _productsPromise;
                _productsPromise = null;
                // Already completed, the future runs the fetch inside this call; otherwise, from its completion.
                var isInCall = _initializedFuture.isCompleted;
                // The restore with the fetch reads the user of the token set at the call, as a request does.
                var update = _accessTokenUpdates;

                _initializedFuture.OnComplete(
                    onSuccess: _ => FetchProducts_(),
                    onError: error => {
                        XsollaLogger.Error(Tag, $"RetrieveProducts failed: {error}");
                        ReportFetchThenReleaseDeliveries(promise, false, () => _callback.OnProductsFetchFailed(error));
                    }
                );

                void FetchProducts_()
                {
                    var productsRestoreFuture =
                        SimpleFuture.Create<XsollaStoreClientPurchasedProduct[], string>(out var productsRestorePromise);
                    var productsRequestFuture =
                        SimpleFuture.Create<XsollaStoreClientProduct[], string>(out var productsRequestPromise);
                    var productsFuture = productsRestoreFuture.Zip(
                        productsRequestFuture, (inventory, store) => (inventory, store)
                    );

                    Exception clientException = null;
                    try
                    {
#if UNITY_IOS
                        // iOS restores automatically on start via the native SDK observer
                        productsRestorePromise.Complete(new XsollaStoreClientPurchasedProduct[0]);
#else
                        if (promise != null && _options.restoresWithFirstFetch)
                        {
                            _storeClient.RestorePurchases(
                                onSuccess: items => productsRestorePromise.Complete(items),
                                onError: error => productsRestorePromise.CompleteWithError(error)
                            );
                        }
                        else
                        {
                            productsRestorePromise.Complete(new XsollaStoreClientPurchasedProduct[0]);
                        }
#endif

                        _storeClient.FetchProducts(skus.ToArray(),
                            onSuccess: items => productsRequestPromise.Complete(items),
                            onError: error => productsRequestPromise.CompleteWithError(error)
                        );
                    }
                    catch (Exception) when (isInCall)
                    {
                        // The client threw before the core registered for its answer, so nothing else releases the
                        // purchases waiting for the first fetch.
                        CompleteWhileThrowing(() => promise?.Complete(false), "RetrieveProducts: a purchase delivery");

                        throw;
                    }
                    catch (Exception e)
                    {
                        clientException = e;
                    }

                    if (clientException == null)
                    {
                        productsFuture.OnComplete(
                            onSuccess: items => {
                                XsollaLogger.Debug(Tag, $"RetrieveProducts finished successfully. " +
                                                $"Restored: {items.inventory.Length}, " +
                                                $"Retrieved: {items.store.Length} ( {XsollaClientHelpers.ToJson(items.store)} )");

                                foreach (var item in items.store)
                                    _productById[item.sku] = item;

                                var inventory = WithNativeIdsOfNonConsumables(items.inventory);
                                var products = items.store.Map(item => ToPurchasingProduct(item, inventory));
                                foreach (var product in products)
                                {
                                    var restoredPurchase = product.restoredPurchase;

                                    // A restored purchase reaches the binding on its product, not through
                                    // OnPurchaseSucceeded, so record the quantity here too. Without this, its consume
                                    // misses the map and consumes 1. With collapse on, the restored quantity is the full
                                    // count and the SKU drains in a single restore; in split mode it is always 1 (only
                                    // the first inventory row per SKU is surfaced).
                                    if (restoredPurchase != null && !string.IsNullOrEmpty(restoredPurchase.transactionId))
                                    {
                                        var purchase = (restoredPurchase.transactionId, product.sku);

                                        TrackQuantity(purchase, restoredPurchase.quantity);
                                        // The hand-over is a delivery too: without the mark, a later RestoreTransactions
                                        // or native re-delivery, or a purchase callback queued behind this fetch, would
                                        // hand it over again.
                                        _deliveredPurchases.Add(purchase);
                                        if (!_options.limitsIdentitylessRestores && !_consumedPurchases.Contains(purchase))
                                            _updateByHandOver[purchase] = update;

                                        // The binding holds these units too, and the Standalone and Android inventory
                                        // restores report them again under transaction ids no dedup recognizes.
                                        if (_options.limitsIdentitylessRestores && !_consumedPurchases.Contains(purchase))
                                        {
                                            if (IsOfCurrentUser(update))
                                                _unconsumedHandOvers[purchase] = restoredPurchase.quantity;
                                            else
                                                SetAside(_switchEpochs[SwitchEpochOf(update)].user, purchase, restoredPurchase.quantity);
                                        }

                                        var isNeverConsumed =
                                            _productTypeBySku.TryGetValue(product.sku, out var productType)
                                            && productType != XsollaPurchasingProductType.Consumable;
                                        var finishOutcome = isNeverConsumed
                                            ? $"{productType}, never consumed"
                                            : $"FinishTransaction consumes {restoredPurchase.quantity} unit(s) if consumable";
                                        XsollaLogger.Debug(Tag, $"RetrieveProducts: tracking restored sku={product.sku} quantity={restoredPurchase.quantity} transactionId={restoredPurchase.transactionId} ({finishOutcome})");
                                    }
                                }

                                ReportFetchThenReleaseDeliveries(promise, true, () => _callback.OnProductsFetched(products));
                            },
                            onError: error => {
                                XsollaLogger.Error(Tag, $"RetrieveProducts failed: {error}");
                                ReportFetchThenReleaseDeliveries(promise, false, () => _callback.OnProductsFetchFailed(error));
                            }
                        );
                    }
                    else
                    {
                        // A rethrow would leave the fetch unanswered and reach the initialization or the configuration's
                        // resolution instead of a caller.
                        XsollaLogger.Error(Tag, $"RetrieveProducts failed: the store client threw: {clientException}");
                        ReportFetchThenReleaseDeliveries(
                            promise,
                            false,
                            () => _callback.OnProductsFetchFailed(clientException.Message)
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Starts a purchase of the given SKU and reports its result through
        /// <see cref="IXsollaPurchasingCoreCallback.OnPurchaseSucceeded"/> or
        /// <see cref="IXsollaPurchasingCoreCallback.OnPurchaseFailed"/>.
        /// <para/>
        /// The request goes to the store client at once, without waiting for <see cref="Initialize"/>. A purchase is
        /// delivered only once the first <see cref="FetchProducts"/> after <see cref="Initialize"/> completes; with
        /// <see cref="XsollaPurchasingCoreOptions.dropsPurchasesBeforeFirstFetch"/>, one reported before that call is
        /// dropped. A purchase the store client returns for this request carries <paramref name="sku"/> as
        /// <see cref="XsollaPurchasingPurchase.requestedSku"/>, also when it reports the purchase under another SKU.
        /// With <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>, the request holds one unit of
        /// <paramref name="sku"/> back from the identity-less purchases of a restore of the user it was requested for
        /// until it is answered (see <see cref="DeliverRestoredPurchases"/>), and an answer after
        /// <see cref="UpdateAccessToken"/> switched to another user hands its units over to that user, set aside
        /// in <see cref="_parkedHandOvers"/>. A request the store client cannot answer with its result
        /// (<see cref="IXsollaStoreClient.CanAnswerPurchase"/>) holds nothing, as it would hold its unit for the session
        /// and keep its paid unit out of every restore; an answer it gets anyway is reported as any other.
        /// </summary>
        /// <param name="sku">SKU of the product to purchase.</param>
        /// <param name="developerPayload">Developer payload passed to the store client, or null.</param>
        /// <param name="args">Purchase arguments.</param>
        public void Purchase(string sku, [CanBeNull] string developerPayload, XsollaStoreClientPurchaseArgs args)
        {
            XsollaLogger.Debug(Tag, "Purchase");

            var update = _accessTokenUpdates;
            var unit = (update, sku);
            var isInFlight = _options.limitsIdentitylessRestores && sku != null && _storeClient.CanAnswerPurchase(sku, args);
            if (isInFlight)
            {
                _purchaseUnitsInFlight.TryGetValue(unit, out var units);
                _purchaseUnitsInFlight[unit] = units + 1;
            }

            try
            {
                _storeClient.PurchaseProduct(sku, developerPayload, args,
                    onSuccess: product =>
                    {
                        // Ends just ahead of the delivery, which waits for the first catalog fetch as a restore's
                        // deliveries do: a restore queued before it still holds the unit, and the hand-over from then on.
                        _productsFuture.OnComplete(onSuccess: _ => EndInFlight(), onError: _ => EndInFlight());
                        OnPurchaseSucceeded(product, sku, update);
                    },
                    onError: error =>
                    {
                        EndInFlight();
                        OnPurchaseFailed(error, sku);
                    }
                );
            }
            catch
            {
                // A request the store client threw on may never be answered, and must not hold a unit for the session.
                EndInFlight();
                throw;
            }

            // A request is answered at most once; a later report under it is not its answer and ends nothing.
            void EndInFlight()
            {
                if (isInFlight)
                {
                    isInFlight = false;

                    var units = _purchaseUnitsInFlight[unit] - 1;
                    if (units > 0)
                        _purchaseUnitsInFlight[unit] = units;
                    else
                        _purchaseUnitsInFlight.Remove(unit);
                }
            }
        }

        /// <summary>
        /// Starts a purchase of <paramref name="productId"/> without a developer payload, as <see cref="Purchase"/> does.
        /// </summary>
        /// <param name="productId">SKU of the product to purchase.</param>
        /// <param name="args">Purchase arguments.</param>
        public void InitiatePurchase(string productId, XsollaStoreClientPurchaseArgs args) =>
            Purchase(productId, null, args);

        /// <summary>
        /// Gets the store client's access token.
        /// </summary>
        /// <param name="onSuccess">Receives the access token, or null.</param>
        /// <param name="onError">Receives the store client's error, or null.</param>
        public void GetAccessToken([CanBeNull] Action<string> onSuccess, [CanBeNull] Action<string> onError)
        {
            XsollaLogger.Debug(Tag, "GetAccessToken");

            _storeClient.GetAccessToken(
                onSuccess: token => onSuccess?.Invoke(token),
                onError: error => onError?.Invoke(error)
            );
        }

        /// <summary>
        /// Hands the store client a new access token.
        /// <para/>
        /// With <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>, a call reads the store client's
        /// current token as well, whatever is held, in flight or open at the time, since a purchase or a restore
        /// answered after the switch is the previous user's even when nothing was handed over yet: the read and the
        /// update go out back to back before this returns, so the store client sees the new token ahead of any call
        /// made after this one, however late the read answers. A call made while an earlier one is not decided yet
        /// reads nothing, since its read may answer a token the earlier update is replacing: its previous token is the
        /// latest earlier call's token if the store client took it, else the previous token that call compared
        /// against, unknown when that one's read failed. Without the option nothing is read: the token is handed over,
        /// and the user it names is recorded for <see cref="Consume"/> (see <see cref="_usersByUpdate"/>). Once the
        /// previous token is known, the store client took the new token and every earlier call is decided, in any
        /// order, and if both tokens name a user (see <see cref="UserOf"/>) and the users differ, the units held when
        /// the update went out are set aside for the previous user, so the new user's own identity-less purchases of
        /// those SKUs are restored, and the units set aside for the new user before are held again, so their restore
        /// does not hand them over a second time; a new switch epoch starts, which the requests and restores sent since the update went out belong to (see <see cref="_switchEpochs"/>). A refresh
        /// of the same user's token, a token naming no readable user, an unknown previous token or a failed update
        /// moves nothing; the read never stops, delays or fails the update, and a later call's switch waits for an
        /// earlier call's update and read to answer. Units handed over while the update is in flight stay held, since a restore answered then may already
        /// be the new user's.
        /// <para/>
        /// Standalone answers the read with the previous token: at once once authenticated, else ahead of the update,
        /// since both wait for the same authentication in call order. Android answers it with the previous token too in
        /// JWT mode, as its bridge answers with the JWT it last handed to the native SDK before the read. A store client
        /// that answers the read only after applying the update reads the new token, so the users match and the units
        /// stay held: the safe side.
        /// </summary>
        /// <param name="token">The new access token.</param>
        /// <param name="onSuccess">Invoked once the store client took the token, or null.</param>
        /// <param name="onError">Receives the store client's error, or null.</param>
        public void UpdateAccessToken(string token, [CanBeNull] Action onSuccess, [CanBeNull] Action<string> onError)
        {
            XsollaLogger.Debug(Tag, "UpdateAccessToken");

            _accessTokenUpdates++;

            if (!_options.limitsIdentitylessRestores)
            {
                var update = _accessTokenUpdates;
                _usersByUpdate.Add(UserOf(token));
                try
                {
                    _storeClient.UpdateAccessToken(
                        token,
                        onSuccess: () => onSuccess?.Invoke(),
                        onError: error => {
                            // The store client kept its previous token, so the user later consumes run under is
                            // unknown and none of them is refused.
                            _usersByUpdate[update] = null;
                            onError?.Invoke(error);
                        }
                    );
                }
                catch
                {
                    // Thrown before or after the store client took the token: unknown as well.
                    _usersByUpdate[update] = null;
                    throw;
                }
            }
            else
            {
                var earlier = _undecidedAccessTokenUpdates.Count > 0
                    ? _undecidedAccessTokenUpdates[_undecidedAccessTokenUpdates.Count - 1]
                    : null;

                // The read's answer changes no hand-over, so this is also what is held when the update goes out.
                var pending = new AccessTokenUpdate(
                    _accessTokenUpdates,
                    token,
                    _unconsumedHandOvers.Keys.ToArray(),
                    earlier
                );
                _undecidedAccessTokenUpdates.Add(pending);

                if (earlier == null)
                {
                    try
                    {
                        _storeClient.GetAccessToken(
                            onSuccess: readToken => OnRead(readToken, null),
                            onError: error => OnRead(null, error)
                        );
                    }
                    catch (Exception e)
                    {
                        if (pending.isRead)
                            XsollaLogger.Warning(Tag, $"UpdateAccessToken: reading the current token threw after answering: {e}");
                        else
                            OnRead(null, e.ToString());
                    }
                }

                try
                {
                    _storeClient.UpdateAccessToken(
                        token,
                        onSuccess: () =>
                        {
                            OnUpdated(true);
                            onSuccess?.Invoke();
                        },
                        onError: error =>
                        {
                            OnUpdated(false);
                            onError?.Invoke(error);
                        }
                    );
                }
                catch
                {
                    // An update that throws before answering is not taken, else it would hold every later switch
                    // undecided; one that answered first keeps its answer.
                    OnUpdated(false);
                    throw;
                }

                // Each answer counts once, also when the read throws after answering or either one answers twice.
                void OnRead([CanBeNull] string readToken, [CanBeNull] string error)
                {
                    if (!pending.isRead)
                    {
                        pending.isRead = true;
                        pending.readToken = readToken;

                        if (error != null)
                            XsollaLogger.Warning(Tag, $"UpdateAccessToken: the current token could not be read, the units handed over stay held: {error}");

                        DecideAccessTokenUpdates();
                    }
                }

                void OnUpdated(bool isTaken)
                {
                    if (pending.isTaken == null)
                    {
                        pending.isTaken = isTaken;
                        DecideAccessTokenUpdates();
                    }
                }
            }
        }

        /// <summary>
        /// Decides the switches of <see cref="_undecidedAccessTokenUpdates"/> in the order they went out, up to the
        /// first one still waiting for the answer of its update or its read (see <see cref="UpdateAccessToken"/>).
        /// </summary>
        private void DecideAccessTokenUpdates()
        {
            var switches = 0;
            while (_undecidedAccessTokenUpdates.Count > 0 && IsAnswered(_undecidedAccessTokenUpdates[0]))
            {
                var pending = _undecidedAccessTokenUpdates[0];
                _undecidedAccessTokenUpdates.RemoveAt(0);

                // The earlier update went out first, so it is decided by now.
                var earlier = pending.earlier;
                if (earlier == null)
                    pending.previousToken = pending.readToken;
                else if (earlier.isTaken == true)
                    pending.previousToken = earlier.token;
                else
                    pending.previousToken = earlier.previousToken;

                var previousUser = UserOf(pending.previousToken);
                var user = UserOf(pending.token);
                if (pending.isTaken == true && previousUser != null && user != null && previousUser != user)
                {
                    // A hand-over consumed since the update went out is gone, and stays gone.
                    var parkedCount = 0;
                    foreach (var handOver in pending.heldHandOvers)
                    {
                        if (_unconsumedHandOvers.TryGetValue(handOver, out var units))
                        {
                            SetAside(previousUser, handOver, units);
                            _unconsumedHandOvers.Remove(handOver);
                            parkedCount++;
                        }
                    }

                    var returnedCount = 0;
                    if (_parkedHandOvers.TryGetValue(user, out var returned))
                    {
                        _parkedHandOvers.Remove(user);
                        foreach (var handOver in returned)
                            _unconsumedHandOvers[handOver.Key] = handOver.Value;

                        returnedCount = returned.Count;
                    }

                    var endedEpoch = _switchEpochs.Count - 1;
                    _switchEpochs[endedEpoch] = (_switchEpochs[endedEpoch].update, previousUser);
                    _switchEpochs.Add((pending.update, user));
                    switches++;

                    XsollaLogger.Debug(Tag, $"UpdateAccessToken: another user's token, set aside the {parkedCount} hand-over(s) of the previous user and held again the {returnedCount} of the new one.");
                }
            }

            // Raised after the loop, so a handler that updates the token again is not decided inside this one.
            for (var i = 0; i < switches; i++)
                UserSwitched?.Invoke();

            static bool IsAnswered(AccessTokenUpdate pending) =>
                pending.isTaken != null && (pending.earlier != null || pending.isRead);
        }

        /// <summary>
        /// Sets a hand-over aside in <see cref="_parkedHandOvers"/> for <paramref name="user"/>, next to the ones set
        /// aside for that user before.
        /// </summary>
        private void SetAside(string user, (string transactionId, string sku) handOver, int units)
        {
            if (!_parkedHandOvers.TryGetValue(user, out var parked))
            {
                parked = new Dictionary<(string transactionId, string sku), int>();
                _parkedHandOvers[user] = parked;
            }

            parked[handOver] = units;
        }

        /// <summary>
        /// The switch epoch (see <see cref="_switchEpochs"/>) of a request or a restore that went out at the
        /// <see cref="_accessTokenUpdates"/> count <paramref name="update"/>: the last one started by that update or an
        /// earlier one.
        /// <para/>
        /// Read when the request or the restore is answered rather than when it goes out, since a switch is decided only
        /// once the update is answered, possibly after calls made behind it.
        /// </summary>
        private int SwitchEpochOf(int update)
        {
            var epoch = _switchEpochs.Count - 1;
            while (_switchEpochs[epoch].update > update)
                epoch--;

            return epoch;
        }

        /// <summary>
        /// Whether the user of the switch epoch of a request or a restore that went out at the
        /// <see cref="_accessTokenUpdates"/> count <paramref name="update"/> (see <see cref="SwitchEpochOf"/>) is the
        /// current one: the epoch is the current one, or a switch came back to its user.
        /// </summary>
        private bool IsOfCurrentUser(int update)
        {
            var epoch = SwitchEpochOf(update);
            var currentEpoch = _switchEpochs.Count - 1;
            return epoch == currentEpoch || _switchEpochs[epoch].user == _switchEpochs[currentEpoch].user;
        }

        /// <summary>
        /// The key of the user of switch epoch <paramref name="epoch"/> (see <see cref="CurrentUserKey"/>): the first
        /// epoch of that user, or the epoch itself while its user is unknown, which only the first epoch's is before the
        /// first switch. Stable once given, as every epoch before the current one has its user.
        /// </summary>
        private int UserKeyOf(int epoch)
        {
            var user = _switchEpochs[epoch].user;
            return user == null ? epoch : _switchEpochs.FindIndex(candidate => candidate.user == user);
        }

        /// <summary>
        /// The user an access token names: the <c>sub</c> claim of a JWT, or null when the token is no JWT or its
        /// payload names no user as a non-empty string.
        /// <para/>
        /// The signature is not checked: the claim only tells two users apart, and the store client is the one that
        /// trusts the token.
        /// </summary>
        /// <param name="token">An access token, or null.</param>
        /// <returns>The user, or null when it cannot be read.</returns>
        [CanBeNull]
        private static string UserOf([CanBeNull] string token)
        {
            var segments = token?.Split('.');
            if (segments == null || segments.Length != 3)
            {
                return null;
            }
            else
            {
                var payload = segments[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                try
                {
                    var user = (JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)))["sub"] as JValue)
                        ?.Value as string;
                    return string.IsNullOrEmpty(user) ? null : user;
                }
                catch (Exception)
                {
                    // Base64 or JSON that does not decode, or a payload that is no JSON object: the user is unknown.
                    return null;
                }
            }
        }

        /// <summary>
        /// Gets the Apple storefront from the store client.
        /// </summary>
        /// <param name="onSuccess">Receives the storefront identifier, or null.</param>
        /// <param name="onError">Receives the store client's error, or null.</param>
        public void GetAppleStorefront([CanBeNull] Action<string> onSuccess, [CanBeNull] Action<string> onError)
        {
            XsollaLogger.Debug(Tag, "GetAppleStorefront");

            _storeClient.GetAppleStorefront(
                onSuccess: storefront => onSuccess?.Invoke(storefront),
                onError: error => onError?.Invoke(error)
            );
        }

        /// <summary>
        /// Consumes a purchase, if its product type needs it, and reports exactly one result per call through
        /// <paramref name="onSuccess"/> or <paramref name="onFailed"/>.
        /// <para/>
        /// A product that is not <see cref="XsollaPurchasingProductType.Consumable"/> needs no consume and succeeds at
        /// once; the first finish of a purchase of it handed over live, while the core keeps a record of non-consumable
        /// purchases, also reads the purchase's holding to record it (see
        /// <see cref="RecordHoldingOfNonConsumablePurchase"/>). A purchase with a transaction id is consumed at most
        /// once per transaction and SKU in a session, for the quantity of the purchase handed to the binding, or of its
        /// latest report while none was (1 when it was never reported): a repeat after its consume succeeded succeeds
        /// at once, a repeat while the consume is in flight completes with that consume's result, and a request after a
        /// failed consume tries again. A purchase without a transaction id is neither deduplicated nor quantity-tracked:
        /// every call consumes one unit, and so does a purchase dropped as a duplicate of an order delivered under
        /// another transaction id.
        /// Without <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>, a consumable handed over from a
        /// request or restore that went out while the access token named another user than the current one (see
        /// <see cref="UserOf"/>) is not consumed and fails, as a consume by SKU would spend a unit of the current user;
        /// it consumes once its own user's token is set again. A token naming no readable user, or a failed
        /// <see cref="UpdateAccessToken"/>, refuses nothing.
        /// The request goes to the store client at once, without waiting for <see cref="Initialize"/>. An exception the
        /// store client throws reaches the caller; unless the consume succeeded before it, a later request tries again.
        /// </summary>
        /// <param name="sku">SKU of the purchased product.</param>
        /// <param name="transactionId">Transaction id of the purchase, or null or empty when the platform reports none.</param>
        /// <param name="productType">Type of the purchased product.</param>
        /// <param name="onSuccess">Invoked when the purchase is consumed or needed no consume, or null.</param>
        /// <param name="onFailed">Receives the store client's error, or why the consume was not sent, or null.</param>
        public void Consume(
            string sku,
            [CanBeNull] string transactionId,
            XsollaPurchasingProductType productType,
            [CanBeNull] Action onSuccess,
            [CanBeNull] Action<string> onFailed
        )
        {
            var purchase = (transactionId, sku);

            // An empty transaction id does not identify a purchase, so such a request is neither deduplicated nor
            // remembered: it consumes one unit.
            var isTracked = !string.IsNullOrEmpty(transactionId);

            if (productType != XsollaPurchasingProductType.Consumable)
            {
                XsollaLogger.Debug(Tag, $"FinishTransaction: sku={sku} transactionId={transactionId} type={productType} — not consumable, nothing to consume.");

                // Non-consumables are never consumed, so drop any tracked quantity here to avoid leaking the entry.
                if (isTracked)
                {
                    _quantityByPurchase.Remove(purchase);
                    _updateByHandOver.Remove(purchase);
                }

                if (_nonConsumableTransactionIds != null
                    && _nonConsumablePurchasesToRecord.TryGetValue(purchase, out var accessTokenUpdatesAtDelivery))
                {
                    _nonConsumablePurchasesToRecord.Remove(purchase);
                    RecordHoldingOfNonConsumablePurchase(
                        _nonConsumableTransactionIds,
                        sku,
                        transactionId,
                        accessTokenUpdatesAtDelivery
                    );
                }

                onSuccess?.Invoke();
            }
            else if (isTracked && _consumedPurchases.Contains(purchase))
            {
                XsollaLogger.Debug(Tag, $"FinishTransaction: ignoring a duplicate finish (state=Done) sku={sku} transactionId={transactionId}");
                onSuccess?.Invoke();
            }
            else if (isTracked && _consumeRequestsInFlight.TryGetValue(purchase, out var waitingRequests))
            {
                XsollaLogger.Debug(Tag, $"FinishTransaction: ignoring a duplicate finish (state=InFlight) sku={sku} transactionId={transactionId}");
                waitingRequests.Add((onSuccess, onFailed));
            }
            else if (isTracked
                     && _updateByHandOver.TryGetValue(purchase, out var handOverUpdate)
                     && _usersByUpdate[handOverUpdate] is string handOverUser
                     && _usersByUpdate[_usersByUpdate.Count - 1] is string currentUser
                     && handOverUser != currentUser)
            {
                // Consumed by SKU under the current token, it would spend a unit of the current user. Its tracked
                // quantity stays for a finish once its own user's token is set again.
                XsollaLogger.Warning(Tag, $"FinishTransaction: transactionId={transactionId} sku={sku} is a consumable of another user, not consumed");
                onFailed?.Invoke($"Order of another user than the signed-in one (transactionId={transactionId} sku={sku}), not consumed: consuming it would spend a unit of the signed-in user; sign that user in again with UpdateAccessToken, then confirm it");
            }
            else
            {
                int quantity;
                string quantitySource;
                if (isTracked && _quantityByPurchase.TryGetValue(purchase, out var tracked))
                {
                    quantity = tracked;
                    quantitySource = quantity > 1 ? "collapsed multi-unit" : "tracked single unit";
                }
                else
                {
                    quantity = 1;
                    quantitySource = "untracked, defaulting to 1";
                }

                XsollaLogger.Debug(Tag, $"FinishTransaction: consuming sku={sku} quantity={quantity} ({quantitySource}) transactionId={transactionId}");

                // Registered before the store client is called, which may call back before it returns. Repeats join
                // this same list while the consume is in flight, so the callbacks below answer all of them.
                var requests = new List<(Action onSuccess, Action<string> onFailed)> { (onSuccess, onFailed) };
                if (isTracked)
                    _consumeRequestsInFlight[purchase] = requests;

                try
                {
                    _storeClient.ConsumeProduct(sku, quantity, transactionId,
                        onSuccess: () =>
                        {
                            if (isTracked)
                            {
                                _consumeRequestsInFlight.Remove(purchase);
                                _consumedPurchases.Add(purchase);

                                // Drop the quantity only after a confirmed consume so a retried request still drains the
                                // full multi-unit quantity instead of defaulting to 1.
                                _quantityByPurchase.Remove(purchase);
                                _updateByHandOver.Remove(purchase);
                                if (_unconsumedHandOvers.TryGetValue(purchase, out var handedOverUnits))
                                {
                                    _unconsumedHandOvers.Remove(purchase);

                                    // A restore issued before this consume may have read the inventory before it too.
                                    foreach (var window in _restoreWindows)
                                    {
                                        if (!window.ownHandOvers.Contains(purchase))
                                        {
                                            window.consumedUnits.TryGetValue(sku, out var consumedUnits);
                                            window.consumedUnits[sku] = consumedUnits + handedOverUnits;
                                        }
                                    }
                                }
                                else
                                {
                                    string drainedUser = null;
                                    foreach (var parked in _parkedHandOvers)
                                    {
                                        if (parked.Value.Remove(purchase))
                                        {
                                            if (parked.Value.Count == 0)
                                                drainedUser = parked.Key;

                                            break;
                                        }
                                    }

                                    if (drainedUser != null)
                                        _parkedHandOvers.Remove(drainedUser);
                                }
                            }

                            XsollaLogger.Debug(Tag, $"FinishTransaction finished: sku={sku} consumed {quantity} unit(s) in a single consume.");

                            foreach (var request in requests)
                            {
                                // One throwing callback must not cost the requests after it their result. Its exception,
                                // most likely the app's own, still reaches Unity whatever the SDK log level.
                                try
                                {
                                    request.onSuccess?.Invoke();
                                }
                                catch (Exception e)
                                {
                                    XsollaLogger.Error(Tag, $"FinishTransaction: a result callback threw: {e}");
                                    UnityEngine.Debug.LogException(e);
                                }
                            }
                        },
                        onError: error =>
                        {
                            // Release the in-flight mark so a later request can retry this purchase, and keep its tracked
                            // quantity for that retry.
                            if (isTracked)
                                _consumeRequestsInFlight.Remove(purchase);

                            XsollaLogger.Error(Tag, $"FinishTransaction failed: sku={sku} quantity={quantity} transactionId={transactionId}: {error}");

                            foreach (var request in requests)
                            {
                                try
                                {
                                    request.onFailed?.Invoke(error);
                                }
                                catch (Exception e)
                                {
                                    XsollaLogger.Error(Tag, $"FinishTransaction: a result callback threw: {e}");
                                    UnityEngine.Debug.LogException(e);
                                }
                            }
                        }
                    );
                }
                catch
                {
                    // Release the in-flight mark so a later request reaches the store client again instead of joining a
                    // consume nobody answers, and keep the tracked quantity for it as a failure does. A mark that no longer
                    // holds these requests was released by an answer before the throw, or belongs to a later consume.
                    if (isTracked
                        && _consumeRequestsInFlight.TryGetValue(purchase, out var inFlight)
                        && ReferenceEquals(inFlight, requests))
                    {
                        _consumeRequestsInFlight.Remove(purchase);
                    }

                    throw;
                }
            }
        }

        /// <summary>
        /// Restores the user's unconsumed purchases and delivers each one like a purchase result: one delivered
        /// before is dropped, and so is every one reported before the first <see cref="FetchProducts"/> after
        /// <see cref="Initialize"/> when <see cref="XsollaPurchasingCoreOptions.dropsPurchasesBeforeFirstFetch"/> is on.
        /// <para/>
        /// With <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>, the restored purchases are
        /// instead delivered together once that first fetch completes, as <see cref="FetchPurchases"/> delivers them,
        /// identity-less ones limited to the units neither handed over yet nor of a purchase waiting for its result,
        /// and <paramref name="onSuccess"/> is called after the deliveries; a restore that answers before that fetch is
        /// answered once it completes, and never when no such fetch is made.
        /// The request goes to the store client at once, without waiting for <see cref="Initialize"/>.
        /// </summary>
        /// <param name="onSuccess">Receives true when the store client returned any purchase, delivered now or not.</param>
        /// <param name="onError">Receives the store client's error.</param>
        public void RestoreTransactions(Action<bool> onSuccess, Action<string> onError)
        {
            XsollaLogger.Debug(Tag, "RestoreTransactions");

            if (_options.limitsIdentitylessRestores)
            {
                RestoreThenDeliver(
                    "RestoreTransactions",
                    deliversOnlyFetchedProducts: false,
                    onDelivered: items => onSuccess?.Invoke(items.Length > 0),
                    onFailed: error => onError?.Invoke(error)
                );
            }
            else
            {
                // The restore reads the user of the token set at the call, as a request does.
                var update = _accessTokenUpdates;
                _storeClient.RestorePurchases(
                    onSuccess: items =>
                    {
                        foreach (var item in WithNativeIdsOfNonConsumables(items))
                        {
                            OnPurchaseSucceeded(item, null, update);
                        }

                        onSuccess?.Invoke(items.Length > 0);
                    },
                    onError: error => onError?.Invoke(error.ToString())
                );
            }
        }

        /// <summary>
        /// Restores the user's unconsumed purchases like <see cref="RestoreTransactions"/> with
        /// <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>, but delivers only the purchases of
        /// products a catalog fetch returned, for the binding's automatic restore after its catalog.
        /// <para/>
        /// A purchase of another SKU is skipped without being recorded as delivered or counted as handed over, so a
        /// later restore or purchases fetch still delivers it, as Unity IAP 4.x's restore with the first catalog fetch
        /// never delivered it and left it to an explicit restore.
        /// </summary>
        /// <param name="onSuccess">Receives true when the store client returned any purchase, delivered now or not.</param>
        /// <param name="onError">Receives the store client's error.</param>
        public void RestoreTransactionsOfFetchedProducts([CanBeNull] Action<bool> onSuccess, [CanBeNull] Action<string> onError)
        {
            XsollaLogger.Debug(Tag, "RestoreTransactionsOfFetchedProducts");

            RestoreThenDeliver(
                "RestoreTransactionsOfFetchedProducts",
                deliversOnlyFetchedProducts: true,
                onDelivered: items => onSuccess?.Invoke(items.Length > 0),
                onFailed: error => onError?.Invoke(error)
            );
        }

        /// <summary>
        /// Fetches the user's unconsumed purchases, delivers each one like a purchase result once the first
        /// <see cref="FetchProducts"/> after <see cref="Initialize"/> completes, then reports every purchase the store
        /// client returned through <see cref="IXsollaPurchasingCoreCallback.OnPurchasesFetched"/>; a store client error
        /// is reported through <see cref="IXsollaPurchasingCoreCallback.OnPurchasesFetchFailed"/>. For the binding that
        /// does not take purchases with the first catalog fetch.
        /// <para/>
        /// Deliveries take the same path and dedup as <see cref="RestoreTransactions"/>: one delivered before is
        /// skipped, and with <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/> an identity-less one
        /// is limited to the units neither handed over yet nor of a purchase waiting for its result. The report lists
        /// every purchase the store client returned, held back or not, and always comes after the deliveries, also when
        /// the store client answers before that first fetch or from inside the binding's catalog callback; a fetch that
        /// answers before that fetch is reported once it completes, and never when no such fetch is made. A delivery
        /// that throws skips the report, and its exception propagates.
        /// The request goes to the store client at once, without waiting for <see cref="Initialize"/>.
        /// </summary>
        public void FetchPurchases()
        {
            XsollaLogger.Debug(Tag, "FetchPurchases");

            RestoreThenDeliver(
                "FetchPurchases",
                deliversOnlyFetchedProducts: false,
                onDelivered: items => _callback.OnPurchasesFetched(items.Map(item => ToPurchasingPurchase(item, null))),
                onFailed: error => _callback.OnPurchasesFetchFailed(error)
            );
        }

        /// <summary>
        /// Restores the user's unconsumed purchases through the store client and, once the first
        /// <see cref="FetchProducts"/> after <see cref="Initialize"/> completes, delivers them through
        /// <see cref="DeliverRestoredPurchases"/>, then passes every purchase the store client returned to
        /// <paramref name="onDelivered"/>; a store client error goes to <paramref name="onFailed"/>.
        /// <para/>
        /// The batch waits for the catalog as a whole rather than each purchase on its own, so the answer can follow the
        /// deliveries, and the identity-less limit sees every purchase handed over before the batch is released. With
        /// <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>, a <see cref="RestoreWindow"/> opens at
        /// the request and closes once the batch is delivered or the restore fails. A store client that throws after
        /// answering has its exception logged instead of propagated, since the answer stands. Only the first answer
        /// of the store client counts, success or error; a later one is logged and ignored.
        /// </summary>
        /// <param name="operation">Name the log lines carry.</param>
        /// <param name="deliversOnlyFetchedProducts">Whether a purchase of a SKU no catalog fetch returned is skipped.</param>
        /// <param name="onDelivered">Receives every purchase the store client returned, after the deliveries.</param>
        /// <param name="onFailed">Receives the store client's error.</param>
        private void RestoreThenDeliver(
            string operation,
            bool deliversOnlyFetchedProducts,
            Action<XsollaStoreClientPurchasedProduct[]> onDelivered,
            Action<string> onFailed
        )
        {
            var window = new RestoreWindow(_accessTokenUpdates);
            if (_options.limitsIdentitylessRestores)
                _restoreWindows.Add(window);

            // A store client answering a request twice would get its identity-less rows delivered twice: their
            // transaction ids are made up anew, and the window takes the first answer's hand-overs as its own.
            var isAnswered = false;
            var answerTaken = false;
            try
            {
                _storeClient.RestorePurchases(
                    onSuccess: items =>
                    {
                        if (IsFirstAnswer($"{items.Length} purchase(s)"))
                        {
                            XsollaLogger.Debug(Tag, $"{operation} finished: {items.Length} purchase(s)");

                            if (!_productsFuture.TryGetValue(out _))
                                XsollaLogger.Debug(Tag, $"{operation}: {items.Length} purchase(s) queued until the first RetrieveProducts");

                            _productsFuture.OnComplete(
                                onSuccess: _ =>
                                {
                                    var restored = WithNativeIdsOfNonConsumables(items);
                                    try
                                    {
                                        DeliverRestoredPurchases(operation, restored, deliversOnlyFetchedProducts, window);
                                    }
                                    finally
                                    {
                                        _restoreWindows.Remove(window);
                                    }

                                    // Put back after the answer, as a restore answered from inside it sets its own.
                                    var outerAnswerIsOfCurrentUser = IsRestoreAnswerOfCurrentUser;
                                    IsRestoreAnswerOfCurrentUser = SwitchEpochOf(window.update) == _switchEpochs.Count - 1;
                                    try
                                    {
                                        onDelivered(restored);
                                    }
                                    finally
                                    {
                                        IsRestoreAnswerOfCurrentUser = outerAnswerIsOfCurrentUser;
                                    }
                                },
                                onError: error =>
                                {
                                    _restoreWindows.Remove(window);
                                    XsollaLogger.Error(Tag, $"{operation} failed: {error}");
                                    onFailed(error);
                                }
                            );
                            answerTaken = true;
                        }
                    },
                    onError: error =>
                    {
                        if (IsFirstAnswer($"error {error}"))
                        {
                            _restoreWindows.Remove(window);
                            XsollaLogger.Error(Tag, $"{operation} failed: {error}");
                            onFailed(error);
                            answerTaken = true;
                        }
                    }
                );
            }
            catch (Exception exception) when (answerTaken)
            {
                // The answer is reported, or queued behind the first catalog fetch, so an exception the store client
                // throws after it must not reach the caller, who would answer the request a second time. An exception
                // before the answer, or from a delivery or the answer itself, still propagates.
                XsollaLogger.Error(Tag, $"{operation}: the store client threw after answering: {exception}");
            }
            catch
            {
                // A request the store client threw on may never be answered, and must not count consumes for the session.
                _restoreWindows.Remove(window);
                throw;
            }

            bool IsFirstAnswer(string answer)
            {
                var isFirst = !isAnswered;
                isAnswered = true;
                if (!isFirst)
                    XsollaLogger.Warning(Tag, $"{operation}: the store client answered the request again ({answer}), ignored");

                return isFirst;
            }
        }

        /// <summary>
        /// Delivers each restored purchase through the purchase path, in order, except the ones of a SKU no catalog
        /// fetch returned when <paramref name="deliversOnlyFetchedProducts"/> is set, and, with
        /// <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>, the identity-less units the binding
        /// already holds or a purchase waiting for its result will deliver.
        /// <para/>
        /// An identity-less purchase carries a transaction id its store client made up, so no dedup sees that a purchase
        /// delivered its unit already: one flagged <see cref="XsollaStoreClientPurchasedProduct.identityless"/> (the
        /// Android inventory restore, whose ids hold for a session only), or one without an order id or a receipt (the
        /// Standalone inventory restore, whose ids are new on every restore, so an earlier restore goes unseen too). The
        /// units of its SKU handed over and not consumed, those handed over and
        /// consumed since the restore was issued (the store client may have read the inventory before the consume; see
        /// <see cref="RestoreWindow"/>), then those of the <see cref="Purchase"/> requests of the SKU not answered yet
        /// (the inventory credits a unit at payment, before the order tracker answers), are therefore taken to be among
        /// the ones the batch reports: the identity-less
        /// purchases of the SKU, in order, deliver only the units beyond that count, a multi-unit one trimmed to the
        /// rest. A purchase with an order id or a receipt is never limited. A unit held for a request that then fails is
        /// not delivered later by this batch; the next restore reports it again.
        /// The count is read as each purchase is reached rather than once per batch, leaving out the batch's own
        /// hand-overs: a delivery callback that restores again from inside this loop hands over units this batch reports
        /// too, and its later purchases must not deliver them a second time, while one that consumes an older hand-over
        /// moves its units from the handed-over count to the consumed one.
        /// A purchase skipped or limited away does not reach the purchase path, so it records no quantity and no dedup
        /// marks it: a later restore delivers it once nothing holds it back.
        /// A restore issued before <see cref="UpdateAccessToken"/> switched to another user read the previous user's
        /// inventory, so it delivers none of its identity-less purchases; the next restore reports them again.
        /// </summary>
        private void DeliverRestoredPurchases(
            string operation,
            XsollaStoreClientPurchasedProduct[] items,
            bool deliversOnlyFetchedProducts,
            RestoreWindow window
        )
        {
            var matchedUnitsBySku = new Dictionary<string, int>();

            foreach (var item in items)
            {
                var isIdentityless = _options.limitsIdentitylessRestores
                                     && item.sku != null
                                     && (item.identityless || (item.orderId <= 0 && string.IsNullOrEmpty(item.receipt)));

                if (deliversOnlyFetchedProducts && (item.sku == null || !_productById.ContainsKey(item.sku)))
                {
                    XsollaLogger.Debug(Tag, $"{operation}: purchase skipped, no catalog fetch returned its product: transactionId={item.transactionId} sku={item.sku}");
                }
                else if (isIdentityless && SwitchEpochOf(window.update) != _switchEpochs.Count - 1)
                {
                    XsollaLogger.Debug(Tag, $"{operation}: identity-less purchase skipped, the restore was issued before the access token switched to another user: transactionId={item.transactionId} sku={item.sku}");
                }
                else if (isIdentityless)
                {
                    // Held units already matched to earlier purchases of this batch are not matched again; the units of
                    // purchases waiting for their result are matched after the handed-over ones.
                    matchedUnitsBySku.TryGetValue(item.sku, out var matchedUnits);
                    var inFlightUnits = _purchaseUnitsInFlight
                        .Where(unit => unit.Key.sku == item.sku && IsOfCurrentUser(unit.Key.update))
                        .Sum(unit => unit.Value);
                    window.consumedUnits.TryGetValue(item.sku, out var consumedUnits);
                    var heldUnits = Math.Max(0, HeldUnits(item.sku) + consumedUnits + inFlightUnits - matchedUnits);
                    var heldInFlightUnits = Math.Min(heldUnits, inFlightUnits);
                    var quantity = NormalizeQuantity(item.quantity);

                    if (heldUnits == 0)
                    {
                        Deliver(item);
                    }
                    else if (quantity <= heldUnits)
                    {
                        matchedUnitsBySku[item.sku] = matchedUnits + quantity;
                        var skippedInFlightUnits = Math.Max(0, quantity - (heldUnits - heldInFlightUnits));
                        XsollaLogger.Debug(Tag, $"{operation}: identity-less purchase skipped, of its {quantity} unit(s) {quantity - skippedInFlightUnits} are handed over and not consumed before the restore was issued, {skippedInFlightUnits} of a purchase waiting for its result: transactionId={item.transactionId} sku={item.sku}");
                    }
                    else
                    {
                        matchedUnitsBySku[item.sku] = matchedUnits + heldUnits;
                        XsollaLogger.Debug(Tag, $"{operation}: identity-less purchase trimmed from {quantity} to {quantity - heldUnits} unit(s), of the {heldUnits} held back {heldUnits - heldInFlightUnits} are handed over and not consumed before the restore was issued, {heldInFlightUnits} of a purchase waiting for its result: transactionId={item.transactionId} sku={item.sku}");
                        Deliver(WithQuantity(item, quantity - heldUnits));
                    }
                }
                else
                {
                    Deliver(item);
                }
            }

            void Deliver(XsollaStoreClientPurchasedProduct product)
            {
                // Marked ahead of the hand-over, as the binding may consume from inside the delivery callback. A
                // purchase this delivery does not hand over has no entry for the mark to leave out or a consume to count.
                var purchase = (product.transactionId, product.sku);
                if (!_unconsumedHandOvers.ContainsKey(purchase))
                    window.ownHandOvers.Add(purchase);

                OnPurchaseSucceeded(product, null, window.update);
            }

            int HeldUnits(string sku)
            {
                var units = 0;
                foreach (var handOver in _unconsumedHandOvers)
                {
                    if (handOver.Key.sku == sku && !window.ownHandOvers.Contains(handOver.Key))
                        units += handOver.Value;
                }

                return units;
            }
        }

        /// <summary>
        /// Looks up a product fetched by any earlier <see cref="FetchProducts"/> call.
        /// </summary>
        /// <param name="sku">SKU of the product.</param>
        /// <param name="product">The fetched product, or null when no fetch returned it.</param>
        /// <returns>True when a fetch returned the product.</returns>
        public bool TryGetProduct(string sku, out XsollaStoreClientProduct product) =>
            _productById.TryGetValue(sku, out product);

        /// <summary>
        /// Whether the resolved configuration turns local purchases restore off
        /// (<see cref="XsollaClientSettings.localPurchasesRestore"/> false); false while the configuration is not resolved
        /// yet or failed to resolve.
        /// </summary>
        /// <remarks>
        /// The core restores whenever the binding asks and leaves the setting to the store client; the property lets a
        /// binding decide not to ask. A catalog fetch succeeds only once the configuration resolved, so after one the
        /// answer is final.
        /// </remarks>
        public bool IsLocalPurchasesRestoreOff =>
            _settingsFuture.TryGetValue(out var configuration) && !configuration.settings.localPurchasesRestore;

        /// <summary>
        /// Raised once for every switch to another user <see cref="UpdateAccessToken"/> decides, which happens only
        /// with <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>; a refresh of the same user's
        /// token, a token naming no readable user, an unknown previous token or a failed update raises nothing.
        /// <para/>
        /// Raised once every decidable update is decided and the core holds the new switch epoch, whose user
        /// <see cref="CurrentUserKey"/> names, so a handler that restores or updates the token again sees the switch; on the store client's answer to the update or the read,
        /// ahead of the update's success callback when the read answered first. A handler must not throw: its exception
        /// propagates into the store client's answer that decided the switch, which skips the success callback when that
        /// answer is the update's.
        /// </summary>
        public event Action UserSwitched;

        /// <summary>
        /// Whether the restore whose answer is running belongs to the current switch epoch, issued behind the update of
        /// the last user switch decided (see <see cref="UserSwitched"/>) or before any, so the purchases it returned
        /// are the current user's: read from inside the success callback of <see cref="RestoreTransactions"/> or
        /// <see cref="RestoreTransactionsOfFetchedProducts"/> or from inside
        /// <see cref="IXsollaPurchasingCoreCallback.OnPurchasesFetched"/>, with
        /// <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>; false outside such an answer.
        /// <para/>
        /// A restore issued before the switch read the previous user's inventory and delivered none of its
        /// identity-less purchases, also after a switch back to that user, so its answer tells nothing about what the
        /// current user owns. Read when the answer runs, after the deliveries, so a switch decided from inside one
        /// counts too.
        /// </summary>
        public bool IsRestoreAnswerOfCurrentUser { get; private set; }

        /// <summary>
        /// The key of the user the purchase whose delivery is running belongs to (see <see cref="CurrentUserKey"/>): read
        /// from inside <see cref="IXsollaPurchasingCoreCallback.OnPurchaseSucceeded"/>; 0 outside a delivery.
        /// <para/>
        /// A purchase belongs to the user of the switch epoch of the request it answers or the restore that reported it
        /// (see <see cref="SwitchEpochOf"/>), so one answered after a switch away from that user is not the current
        /// user's, while it is again once a switch comes back to that user.
        /// </summary>
        public int PurchaseUserKey { get; private set; }

        /// <summary>
        /// The key of the current user: the first switch epoch (see <see cref="_switchEpochs"/>) of the user the access
        /// token names, so a switch back to a user gives that user's key again. Two keys are equal exactly when they
        /// stand for one user.
        /// <para/>
        /// Always 0 without <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>, or while no switch was
        /// decided: a token that names no user, or only one user, decides none, and the first epoch's user is read only
        /// at the first switch.
        /// </summary>
        public int CurrentUserKey => UserKeyOf(_switchEpochs.Count - 1);

        /// <summary>
        /// Delivers a purchase the store client reported once the first catalog fetch completes, unless it was
        /// delivered before.
        /// </summary>
        /// <param name="product">The reported purchase.</param>
        /// <param name="requestedSku">SKU of the purchase request it answers, or null when it answers none.</param>
        /// <param name="update">
        /// The <see cref="_accessTokenUpdates"/> count that tells the switch epoch the purchase belongs to (see
        /// <see cref="SwitchEpochOf"/>): of the request it answers or the restore that reported it, else the current
        /// one. A hand-over of another user's epoch is set aside for that user.
        /// </param>
        private void OnPurchaseSucceeded(XsollaStoreClientPurchasedProduct product, [CanBeNull] string requestedSku, int update)
        {
            XsollaLogger.Debug(Tag, $"Purchase finished: {product}");

            var purchase = (product.transactionId, product.sku);
            var isTracked = !string.IsNullOrEmpty(product.transactionId);
            var order = (product.orderId, product.sku);
            var isOrderTracked = _options.deduplicatesByOrderId && product.orderId > 0;

            // A report of a purchase already handed over must not overwrite the quantity handed over. Recorded before
            // the hand-over too, for a consume that comes first: of a purchase dropped below, or of one the binding
            // took from OnPurchasesFetched.
            if (isTracked && !_deliveredPurchases.Contains(purchase))
                TrackQuantity(purchase, NormalizeQuantity(product.quantity));

            if (_options.dropsPurchasesBeforeFirstFetch && _productsPromise != null)
            {
                // Not marked as delivered, so the next restore delivers it.
                XsollaLogger.Debug(Tag, $"Purchase dropped, reported before the first RetrieveProducts: transactionId={product.transactionId} sku={product.sku}");
            }
            else
            {
                if (_productsPromise != null)
                    XsollaLogger.Debug(Tag, $"Purchase queued until the first RetrieveProducts: transactionId={product.transactionId} sku={product.sku}");

                // Delivered whether or not the first fetch loaded the catalog.
                _productsFuture.OnComplete(
                    onSuccess: _ =>
                    {
                        // Marked here rather than on entry: the first fetch marks its restored purchases when it
                        // completes, just before queued deliveries run, and those must still dedup against them.
                        if (isTracked && !_deliveredPurchases.Add(purchase))
                        {
                            XsollaLogger.Warning(Tag, $"Duplicate delivery ignored: transactionId={product.transactionId} sku={product.sku}");
                        }
                        else if (isOrderTracked && !_deliveredOrders.Add(order))
                        {
                            // The consume drains the quantity of the transaction the order was delivered under. This
                            // one stays marked as delivered, so a later report of it records no quantity again.
                            if (isTracked)
                                _quantityByPurchase.Remove(purchase);

                            XsollaLogger.Warning(Tag, $"Duplicate delivery ignored: orderId={product.orderId} transactionId={product.transactionId} sku={product.sku}");
                        }
                        else
                        {
                            // A later report of this purchase, queued behind this one, has already recorded its own
                            // quantity on arrival; the purchase handed over decides what the consume drains. Recorded
                            // before the hand-over, as the binding may consume from inside the callback.
                            if (isTracked)
                            {
                                TrackQuantity(purchase, NormalizeQuantity(product.quantity));

                                // A purchase consumed before its hand-over is not held by the binding, and no consume
                                // would drop its entry.
                                if (_options.limitsIdentitylessRestores && !_consumedPurchases.Contains(purchase))
                                {
                                    if (IsOfCurrentUser(update))
                                        _unconsumedHandOvers[purchase] = NormalizeQuantity(product.quantity);
                                    else
                                        SetAside(_switchEpochs[SwitchEpochOf(update)].user, purchase, NormalizeQuantity(product.quantity));
                                }

                                if (!_options.limitsIdentitylessRestores && !_consumedPurchases.Contains(purchase))
                                    _updateByHandOver[purchase] = update;

                                if (_nonConsumableTransactionIds != null
                                    && product.status != XsollaStoreClientPurchasedProduct.Status.Restored
                                    && product.sku != null
                                    && _productTypeBySku.TryGetValue(product.sku, out var productType)
                                    && productType != XsollaPurchasingProductType.Consumable)
                                {
                                    _nonConsumablePurchasesToRecord[purchase] = _accessTokenUpdates;
                                }
                            }

                            // Put back after the delivery, as a purchase delivered from inside it sets its own.
                            var outerPurchaseUserKey = PurchaseUserKey;
                            PurchaseUserKey = UserKeyOf(SwitchEpochOf(update));
                            try
                            {
                                _callback.OnPurchaseSucceeded(ToPurchasingPurchase(product, requestedSku));
                            }
                            finally
                            {
                                PurchaseUserKey = outerPurchaseUserKey;
                            }
                        }
                    },
                    onError: error =>
                    {
                        XsollaLogger.Error(Tag, $"OnPurchaseSucceeded failed: {error}");
                    }
                );
            }
        }

        /// <summary>
        /// Records the quantity <see cref="Consume"/> drains for a purchase, unless its consume already succeeded:
        /// <see cref="Consume"/> never reads it again then, so the entry would only leak.
        /// </summary>
        private void TrackQuantity((string transactionId, string sku) purchase, int quantity)
        {
            if (!_consumedPurchases.Contains(purchase))
                _quantityByPurchase[purchase] = quantity;
        }

        /// <summary>
        /// Runs <paramref name="report"/>, the binding callback that reports a fetch, then completes
        /// <paramref name="promise"/>, when there is one, with <paramref name="fetched"/>, also when the callback throws.
        /// <para/>
        /// The binding may run the app's own callbacks inside its callback (Unity IAP 4.x runs the app's
        /// <c>IStoreListener</c> inside <c>OnProductsRetrieved</c>), and one that throws must not hold back purchase
        /// delivery for the session. Its exception still propagates unchanged once the completion has run every held
        /// delivery; one that throws meanwhile is reported instead, through <see cref="CompleteWhileThrowing"/>.
        /// </summary>
        private static void ReportFetchThenReleaseDeliveries(
            [CanBeNull] ISimplePromise<bool, string> promise,
            bool fetched,
            Action report
        )
        {
            var isReported = false;
            try
            {
                report();
                isReported = true;
            }
            finally
            {
                if (isReported)
                    promise?.Complete(fetched);
                else
                    CompleteWhileThrowing(() => promise?.Complete(fetched), "RetrieveProducts: a purchase delivery");
            }
        }

        /// <summary>
        /// Runs <paramref name="complete"/>, a promise completion, while another exception propagates, reporting a
        /// callback of the promise that throws, described by <paramref name="callback"/>, rather than letting it
        /// replace that exception.
        /// <para/>
        /// The completion runs every callback, also those queued behind a throwing one, and rethrows the first
        /// callback exception, which is what gets reported here: logged, and passed to Unity as an exception whatever
        /// the SDK log level, as it would be had it escaped, since it most likely comes from the app's own callbacks.
        /// </summary>
        private static void CompleteWhileThrowing(Action complete, string callback)
        {
            try
            {
                complete();
            }
            catch (Exception e)
            {
                XsollaLogger.Error(Tag, $"{callback} threw: {e}");
                UnityEngine.Debug.LogException(e);
            }
        }

        private void OnPurchaseFailed(string error, [CanBeNull] string sku)
        {
            var parsed = XsollaStoreClientHelpers.ParsePurchaseError(error);
            var reason = MapToPurchasingReason(parsed);

            if (reason == XsollaPurchasingFailureReason.UserCancelled) {
                XsollaLogger.Warning(Tag, $"Purchase failed: {parsed.message}, reason: {reason}, sku: {sku}");
            } else {
                XsollaLogger.Error(Tag, $"Purchase failed: {parsed.message}, reason: {reason}, sku: {sku}");
            }

            if (sku != null)
                _callback.OnPurchaseFailed(new XsollaPurchasingFailure(sku, reason, parsed.message));
        }

        /// <summary>
        /// A cancellation code wins over any reason the platform client named, so a cancel keeps one meaning whatever a
        /// client tags; otherwise the named reason, else <see cref="XsollaPurchasingFailureReason.Unknown"/>.
        /// </summary>
        private static XsollaPurchasingFailureReason MapToPurchasingReason(XsollaStoreClientError parsed) =>
            parsed.code == XsollaStoreClientPurchaseErrorCode.Cancelled
                ? XsollaPurchasingFailureReason.UserCancelled
                : parsed.reason ?? XsollaPurchasingFailureReason.Unknown;

        private static XsollaPurchasingPurchase ToPurchasingPurchase(
            XsollaStoreClientPurchasedProduct product,
            [CanBeNull] string requestedSku
        ) =>
            new XsollaPurchasingPurchase(
                sku: product.sku,
                transactionId: product.transactionId,
                receipt: product.ToReceipt().ToJson(),
                quantity: NormalizeQuantity(product.quantity),
                requestedSku: requestedSku
            );

        /// <summary>
        /// A reported quantity below 1 means the platform sent none; the purchase covers one unit.
        /// </summary>
        private static int NormalizeQuantity(int quantity) => quantity > 0 ? quantity : 1;

        /// <summary>
        /// A copy of <paramref name="product"/> covering <paramref name="quantity"/> units, so its receipt and the
        /// quantity its consume drains both carry the trimmed count.
        /// </summary>
        private static XsollaStoreClientPurchasedProduct WithQuantity(XsollaStoreClientPurchasedProduct product, int quantity) =>
            XsollaStoreClientPurchasedProduct.Builder.Create()
                .SetSku(product.sku)
                .SetOrderId(product.orderId)
                .SetInvoiceId(product.invoiceId)
                .SetTransactionId(product.transactionId)
                .SetReceipt(product.receipt)
                .SetQuantity(quantity)
                .SetPurchaseTimeMillis(product.purchaseTimeMillis)
                .SetStatus(product.status)
                .SetOrigin(product.origin)
                .SetDeveloperPayload(product.developerPayload)
                .SetIdentityless(product.identityless)
                .Build();

        /// <summary>
        /// The restored <paramref name="items"/>, with <see cref="XsollaPurchasingCoreOptions.restoresNonConsumablesUnderNativeIds"/>
        /// each identity-less one of a SKU typed <see cref="XsollaPurchasingProductType.NonConsumable"/> or
        /// <see cref="XsollaPurchasingProductType.Subscription"/> put back under the id Unity IAP 4.x's transaction log
        /// holds for it: the transaction id of the purchase this device granted it as, recorded in
        /// <see cref="_nonConsumableTransactionIds"/>, or else the id the Android SDK made up for it, which earlier
        /// launches restored it under. The others as they are.
        /// <para/>
        /// <see cref="XsollaRestoredTransactionIdsAndroid"/> keeps the made-up id as the invoice id, and the Android
        /// SDK reported it as order and transaction id alike, so the purchase reads as the Android SDK delivered it.
        /// The store client's receipt cache keeps its line under the id it gave, which only a consume reads, and a
        /// non-consumable is never consumed.
        /// <para/>
        /// Without the option, <paramref name="items"/> itself.
        /// </summary>
        private XsollaStoreClientPurchasedProduct[] WithNativeIdsOfNonConsumables(XsollaStoreClientPurchasedProduct[] items)
        {
            if (!_options.restoresNonConsumablesUnderNativeIds)
            {
                return items;
            }
            else
            {
                return items.Map(item =>
                {
                    if (item.sku != null
                        && _productTypeBySku.TryGetValue(item.sku, out var productType)
                        && productType != XsollaPurchasingProductType.Consumable
                        && HasMadeUpId(item, out var nativeId))
                    {
                        var transactionId = _nonConsumableTransactionIds?.Find(item.sku, item.invoiceId)
                                            ?? item.invoiceId;

                        XsollaLogger.Debug(Tag, $"Restored {productType} under the id Unity IAP logged: sku={item.sku} transactionId={item.transactionId} nativeTransactionId={item.invoiceId} -> transactionId={transactionId}");

                        return XsollaStoreClientPurchasedProduct.Builder.Create()
                            .SetSku(item.sku)
                            .SetOrderId(nativeId)
                            .SetInvoiceId(item.invoiceId)
                            .SetTransactionId(transactionId)
                            .SetReceipt(item.receipt)
                            .SetQuantity(item.quantity)
                            .SetPurchaseTimeMillis(item.purchaseTimeMillis)
                            .SetStatus(item.status)
                            .SetOrigin(item.origin)
                            .SetDeveloperPayload(item.developerPayload)
                            .Build();
                    }
                    else
                    {
                        return item;
                    }
                });
            }
        }

        /// <summary>
        /// Reads the inventory and records <paramref name="transactionId"/>, a purchase of non-consumable
        /// <paramref name="sku"/> the binding has just finished, for every unit of <paramref name="sku"/> the Android
        /// SDK made up an id for, so the next launches restore the holding under the id Unity IAP 4.x logged and do not
        /// grant it again.
        /// <para/>
        /// Read now rather than at the next restore: the made-up id names the user who holds the item now, and a later
        /// restore may read another user's holding. Every unit of the SKU is recorded, as a non-consumable is either
        /// owned or not: a unit held before this purchase reached Unity IAP through an earlier restore already.
        /// <para/>
        /// Nothing is recorded under an access token that names no user, whose made-up ids every user shares, nor once
        /// <see cref="UpdateAccessToken"/> was called after the delivery, which
        /// <paramref name="accessTokenUpdatesAtDelivery"/> tells, as the read may then answer with another user's
        /// holding: either record would hand that user's restore to Unity IAP under an id it already logged, which it
        /// never grants. A read that is skipped, fails or lands before the inventory shows the purchase records
        /// nothing, and the next launch grants the item once more, as before.
        /// </summary>
        private void RecordHoldingOfNonConsumablePurchase(
            XsollaNonConsumableTransactionIdsAndroid nonConsumableTransactionIds,
            string sku,
            string transactionId,
            int accessTokenUpdatesAtDelivery
        )
        {
            _storeClient.GetAccessToken(
                onSuccess: token =>
                {
                    if (XsollaNonConsumableTransactionIdsAndroid.NamesUser(token))
                    {
                        _storeClient.RestorePurchases(
                            onSuccess: items =>
                            {
                                if (_accessTokenUpdates == accessTokenUpdatesAtDelivery)
                                    RecordHolding(nonConsumableTransactionIds, sku, transactionId, items);
                                else
                                    XsollaLogger.Warning(Tag, $"The access token was updated since the purchase, so its holding is not recorded and its next restore grants it again: sku={sku} transactionId={transactionId}");
                            },
                            onError: error => XsollaLogger.Warning(Tag, $"Could not read the holding of a purchased non-consumable, its next restore grants it again: sku={sku} transactionId={transactionId} error={error}")
                        );
                    }
                    else
                    {
                        XsollaLogger.Warning(Tag, $"The access token names no user, so the holding of a purchased non-consumable is not recorded and its next restore grants it again: sku={sku} transactionId={transactionId}");
                    }
                },
                onError: error => XsollaLogger.Warning(Tag, $"Could not read the access token, so the holding of a purchased non-consumable is not recorded and its next restore grants it again: sku={sku} transactionId={transactionId} error={error}")
            );
        }

        /// <summary>
        /// Records <paramref name="transactionId"/> for every unit of <paramref name="sku"/> in the inventory
        /// <paramref name="items"/> the Android SDK made up an id for.
        /// </summary>
        private static void RecordHolding(
            XsollaNonConsumableTransactionIdsAndroid nonConsumableTransactionIds,
            string sku,
            string transactionId,
            XsollaStoreClientPurchasedProduct[] items
        )
        {
            var recorded = 0;
            foreach (var item in items)
            {
                if (item.sku == sku && HasMadeUpId(item, out _))
                {
                    nonConsumableTransactionIds.Record(sku, item.invoiceId, transactionId);
                    recorded++;
                }
            }

            XsollaLogger.Debug(Tag, $"Recorded the holding of a purchased non-consumable: sku={sku} transactionId={transactionId} units={recorded}");
        }

        /// <summary>
        /// Whether <paramref name="item"/> is a restored unit whose ids the Android SDK made up, kept by
        /// <see cref="XsollaRestoredTransactionIdsAndroid"/> as the invoice id, which is given as
        /// <paramref name="nativeId"/>.
        /// </summary>
        private static bool HasMadeUpId(XsollaStoreClientPurchasedProduct item, out long nativeId) =>
            long.TryParse(item.invoiceId, NumberStyles.None, CultureInfo.InvariantCulture, out nativeId)
            && nativeId > 0
            && item.identityless;

        private static XsollaPurchasingProduct ToPurchasingProduct(
            XsollaStoreClientProduct item,
            XsollaStoreClientPurchasedProduct[] inventory
        )
        {
            // Only the first inventory row per SKU is surfaced here.
            var restoredPurchase = inventory.FindFirst(it => it.sku == item.sku, out var purchasedItem)
                ? ToPurchasingPurchase(purchasedItem, null)
                : null;

            // The price before discount (price, formattedPrice), not priceWithDiscount, as the store has always
            // reported it.
            return new XsollaPurchasingProduct(
                sku: item.sku,
                title: item.localizedTitle,
                description: item.localizedDescription,
                priceString: item.localizedPriceString,
                currencyCode: item.currencyCode,
                price: (decimal) item.localizedPrice / 1_000_000,
                restoredPurchase: restoredPurchase
            );
        }

        /// <summary>
        /// An <see cref="UpdateAccessToken"/> call with <see cref="XsollaPurchasingCoreOptions.limitsIdentitylessRestores"/>
        /// and what its switch is decided from (see <see cref="DecideAccessTokenUpdates"/>).
        /// </summary>
        private sealed class AccessTokenUpdate
        {
            /// <summary>The <see cref="_accessTokenUpdates"/> count of the update.</summary>
            public readonly int update;

            /// <summary>The token handed to the store client.</summary>
            public readonly string token;

            /// <summary>The hand-overs held when the update went out, the only ones its switch sets aside.</summary>
            public readonly (string transactionId, string sku)[] heldHandOvers;

            /// <summary>
            /// The latest update still undecided when this one went out, which tells the previous token instead of a
            /// read; null when this one reads.
            /// </summary>
            [CanBeNull]
            public readonly AccessTokenUpdate earlier;

            /// <summary>Whether the read answered, with a token or an error.</summary>
            public bool isRead;

            /// <summary>The token the read answered, null when it failed or has not answered.</summary>
            [CanBeNull]
            public string readToken;

            /// <summary>Whether the store client took the token; null until it answers.</summary>
            public bool? isTaken;

            /// <summary>The token the store client held before this update, set once decided; null when unknown.</summary>
            [CanBeNull]
            public string previousToken;

            /// <summary>Records an update that went out at the count <paramref name="update"/>.</summary>
            public AccessTokenUpdate(
                int update,
                string token,
                (string transactionId, string sku)[] heldHandOvers,
                [CanBeNull] AccessTokenUpdate earlier
            )
            {
                this.update = update;
                this.token = token;
                this.heldHandOvers = heldHandOvers;
                this.earlier = earlier;
            }
        }

        /// <summary>
        /// What happened to the units handed over between a restore's request and the delivery of its batch, and the
        /// <see cref="_accessTokenUpdates"/> count the request went out at (see <see cref="SwitchEpochOf"/>).
        /// <para/>
        /// The store client reads the inventory somewhere in between, so a unit consumed then may still be in the batch
        /// as an identity-less purchase, with no hand-over left to hold it back: the window keeps holding it. A unit the
        /// batch itself handed over is no such unit, so its consume is not counted.
        /// </summary>
        private sealed class RestoreWindow
        {
            /// <summary>The <see cref="_accessTokenUpdates"/> count the restore was issued at.</summary>
            public readonly int update;

            /// <summary>Units consumed since the request, by SKU, of hand-overs other than the batch's own.</summary>
            public readonly Dictionary<string, int> consumedUnits = new Dictionary<string, int>();

            /// <summary>The purchases the batch's own deliveries handed over.</summary>
            public readonly HashSet<(string transactionId, string sku)> ownHandOvers =
                new HashSet<(string transactionId, string sku)>();

            /// <summary>Opens a window for a restore issued at the update count <paramref name="update"/>.</summary>
            public RestoreWindow(int update)
            {
                this.update = update;
            }
        }
    }
}
