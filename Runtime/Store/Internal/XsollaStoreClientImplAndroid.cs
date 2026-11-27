#if UNITY_ANDROID || UNITY_EDITOR
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using Xsolla.SDK.Common;
using Xsolla.SDK.Common.Extensions;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Store
{
    internal class XsollaStoreClientImplAndroid : IXsollaStoreClient
    {
        private const string Tag = "XsollaStoreClientImplAndroid";

        #region Details

        private sealed class InitData
        {
            [NotNull] public readonly XsollaClientBridgeHelpersAndroid.XsollaUnityBridgeJsonCallback stateListener;
            [NotNull] public readonly XsollaClientBridgeHelpersAndroid.XsollaUnityBridgeJsonCallback paymentListener;

            /// <summary>
            /// Purchase requests waiting for the payment listener's result.
            /// </summary>
            [NotNull] public readonly XsollaPurchaseRequestQueue purchaseRequests;

            [NotNull] public readonly ISimpleFuture<bool, string> initializeFuture;

            /// <summary>
            /// Whether the session runs in <see cref="XsollaClientConfiguration.SimpleMode.WebShop"/> mode, where the bridge
            /// opens the web shop for every purchase without a payment token, and the Android SDK reports no result of it.
            /// </summary>
            public readonly bool isWebShopMode;

            /// <summary>
            /// Purchase tokens of the session, null until the first purchase result or restore answer.
            /// </summary>
            [CanBeNull] public XsollaReceiptCacheAndroid receiptCache;

            /// <summary>
            /// The transaction ids given this session to the restored units the Android SDK made up ids for. A new
            /// session, through <see cref="Initialize"/>, starts with none, so every unit it restores is new to Unity IAP.
            /// </summary>
            [NotNull] public readonly XsollaRestoredTransactionIdsAndroid restoredTransactionIds = new XsollaRestoredTransactionIdsAndroid();

            public InitData(
                [NotNull] XsollaClientBridgeHelpersAndroid.XsollaUnityBridgeJsonCallback stateListener,
                [NotNull] XsollaClientBridgeHelpersAndroid.XsollaUnityBridgeJsonCallback paymentListener,
                [NotNull] XsollaPurchaseRequestQueue purchaseRequests,
                [NotNull] ISimpleFuture<bool, string> initializeFuture,
                bool isWebShopMode
            ) {
                this.stateListener = stateListener;
                this.paymentListener = paymentListener;
                this.purchaseRequests = purchaseRequests;
                this.initializeFuture = initializeFuture;
                this.isWebShopMode = isWebShopMode;
            }

            /// <summary>
            /// Whether a purchase with <paramref name="args"/> opens the web shop, whose result the Android SDK never
            /// reports. A purchase with a payment token runs the token flow even in WebShop mode.
            /// </summary>
            public bool IsWebShopPurchase(XsollaStoreClientPurchaseArgs args) =>
                isWebShopMode && string.IsNullOrEmpty(args.paymentToken);
        }

        /// <summary>
        /// Ends the session of <paramref name="initData"/>, if there is one: each purchase waiting for its result fails once
        /// as <see cref="XsollaStoreClientPurchaseErrorCode.Aborted"/>, naming <paramref name="cause"/>, and the payment
        /// results the session reports afterwards are dropped.
        /// <para/>
        /// The Android SDK drops the results of a session it deinitializes, so the waiting purchases would never get one.
        /// A purchase paid meanwhile stays unconsumed, and the next session's restore delivers it.
        /// </summary>
        private static void EndSession([CanBeNull] InitData initData, string cause)
        {
            initData?.purchaseRequests.Close(XsollaClientHelpers.ToJson(
                new XsollaStoreClientError($"Purchase aborted: {cause}", XsollaStoreClientPurchaseErrorCode.Aborted)
            ));
        }

        #endregion

        #region Initialize
        
        [CanBeNull] private InitData m_InitData;

        [Serializable]
        public class AdditionalSettings
        {
            [CanBeNull, UsedImplicitly] public RetryPolicies retryPolicies;

            // Unity governs the per-SKU fetch tunables for every platform: nulls are resolved to the
            // Unity-side defaults here so native always receives concrete values and applies no defaults
            // of its own.
            [CanBeNull, UsedImplicitly] public ResolvedProductFetchSettings productFetchSettings;

            [Serializable]
            public class ResolvedProductFetchSettings
            {
                [UsedImplicitly] public int maxItemsPerRequest;
                [UsedImplicitly] public int maxParallelRequests;
                [UsedImplicitly] public long cacheTtlMillis;
            }

            [CanBeNull]
            public static AdditionalSettings FromExtensions()
            {
                var extensionsHandler = XsollaStoreClientExtensionsProvider.Handler;
                if (extensionsHandler == null)
                    return null;

                var fetch = extensionsHandler.GetProductFetchSettings();
                return new AdditionalSettings
                {
                    retryPolicies = extensionsHandler.GetRetryPolicies(),
                    productFetchSettings = new ResolvedProductFetchSettings
                    {
                        maxItemsPerRequest = fetch?.EffectiveMaxItemsPerRequest
                            ?? ProductFetchSettings.DefaultMaxItemsPerRequest,
                        maxParallelRequests = fetch?.EffectiveMaxParallelRequests
                            ?? ProductFetchSettings.DefaultMaxParallelRequests,
                        cacheTtlMillis = fetch?.EffectiveCacheTtlMillis
                            ?? ProductFetchSettings.DefaultCacheTtlMillis
                    }
                };
            }
        }

        public static event Action<string, object> onNativePaymentEvent;

        [Serializable]
        public class NativePaymentEvent
        {
            /// <summary>
            /// Must match the Java implementation.
            /// </summary>
            public enum Type
            {
                Opened,
                Loaded,
                Completed,
                Cancelled
            }

            [JsonConverter(typeof(StringEnumConverter))] public Type type;

            public string payloadJson;

            [CanBeNull]
            public static NativePaymentEvent FromJson(string json)
            {
                return !string.IsNullOrEmpty(json)
                    ? XsollaClientHelpers.FromJson<NativePaymentEvent>(json)
                    : null;
            }
        }

        // All currently active `XsollaStoreClientImplAndroid` instances.
        private static readonly List<XsollaStoreClientImplAndroid> instances = new();

        public void Initialize(XsollaClientConfiguration configuration,
            InitializeResultFunc onSuccess, ErrorFunc onError,
            PurchaseProductResultFunc onSuccessPurchaseProduct, ErrorFunc onErrorPurchase
        ) {
            XsollaLogger.Debug(Tag, $"Initialize: {configuration}");

            RunOnStartThread.Create();

            XsollaClientCallback.CallbackDelegate onState = (result, error) => {
                XsollaLogger.Debug(Tag, $"State Listener: {LogObfuscation.ObfuscateJsonIfEnabled(result)} {error}");
            };

            var purchaseRequests = new XsollaPurchaseRequestQueue();

            XsollaClientCallback.CallbackDelegate onPayment = (result, error) => {
                if (!purchaseRequests.TryAnswer(result, error)) {
                    XsollaLogger.Debug(Tag, $"Payment Listener: common callback fire result={LogObfuscation.ObfuscateJsonIfEnabled(result)} error={error}");

                    if (onSuccessPurchaseProduct != null || onErrorPurchase != null)
                    {
                        RunOnStartThread.Run(() => {
                            if (result != null)
                            {
                                XsollaLogger.Debug(Tag, $"Payment Listener: result={LogObfuscation.ObfuscateJsonIfEnabled(result)}");
                                // Cached like a requested purchase, or `ConsumeProduct` meets a non-null cache
                                // without it, fails as unknown, and the next restore grants the item again.
                                var product = XsollaStoreClientHelpers.JsonToPurchase(result, null);
                                AddReceiptToCache(product.transactionId, product.sku, product.receipt, restoredQuantity: 0);
                                onSuccessPurchaseProduct?.Invoke(product);
                            }
                            else
                            {
                                XsollaClientBridgeHelpersAndroid.ReportError(Tag, $"Payment Listener: failed={error}");
                                onErrorPurchase?.Invoke(error ?? string.Empty);
                            }
                        });
                    }
                }
            };
            
            var initializeFuture = SimpleFuture.Create<bool, string>(out var promise);

            // A re-Initialize leaves the client not initialized until the new session's arguments are built, so a failure to
            // build them fails its purchases as unavailable and its fetches and restores as not initialized.
            var endedInitData = m_InitData;
            m_InitData = null;
            EndSession(endedInitData, "the store client was initialized again");

            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "Initialize",
                onError: error => {
                    // The listener never answers a call that threw, so fetches and restores waiting on the future would hang.
                    if (!initializeFuture.isCompleted)
                        promise.CompleteWithError(error);
                    onError?.Invoke(error);
                },
                args: () => {
                    var configurationJson = XsollaClientHelpers.ConfigurationToJson(configuration);
                    var additionalSettingsJson = XsollaClientHelpers.ToJson(AdditionalSettings.FromExtensions());
                    var stateListener = XsollaClientBridgeHelpersAndroid.CreateCommonListener("State Listener", onState);
                    var paymentListener = XsollaClientBridgeHelpersAndroid.CreateCommonListener("Payment Listener", onPayment);
                    var paymentEventCallback = CreatePaymentEventCallback();

                    m_InitData = new InitData(
                        stateListener,
                        paymentListener,
                        purchaseRequests,
                        initializeFuture,
                        configuration.simpleMode == XsollaClientConfiguration.SimpleMode.WebShop
                    );

                    XsollaClientBridgeHelpersAndroid.AddCommonListenerCallback(
                        stateListener,
                        "Initialize",
                        onSuccess: _ => {
                            onSuccess?.Invoke();
                            promise.Complete(true);
                        },
                        onError: error => {
                            onError?.Invoke(error);
                            promise.CompleteWithError(error);
                        }
                    );

                    return new object[] {
                        configurationJson, additionalSettingsJson, stateListener, paymentListener, paymentEventCallback
                    };
                }
            );

            if (!instances.Contains(this))
                instances.Add(this);


            XsollaClientBridgeHelpersAndroid.XsollaUnityBridgeJsonCallback CreatePaymentEventCallback()
            {
                const string TAG = "PaymentEventCallback";

                return XsollaClientBridgeHelpersAndroid.CreateCallback(
                    logTag: TAG,
                    onSuccess: eventJson => {
                        var nativePaymentEvent = NativePaymentEvent.FromJson(eventJson);
                        if (nativePaymentEvent == null) {
                            XsollaLogger.Warning(TAG, $"Failed to parse native payment event: {LogObfuscation.ObfuscateJsonIfEnabled(eventJson)}");
                            return;
                        }

                        var paymentEvent = nativePaymentEvent.type switch {
                            NativePaymentEvent.Type.Opened => "PaystationOpen",
                            NativePaymentEvent.Type.Loaded => "PaystationLoaded",
                            NativePaymentEvent.Type.Completed => "PaystationCompleted",
                            NativePaymentEvent.Type.Cancelled => "PaystationCancelled",
                            _ => null
                        };

                        if (paymentEvent == null) {
                            XsollaLogger.Warning(TAG, $"Unsupported payment event type: {nativePaymentEvent.type}, {LogObfuscation.ObfuscateJsonIfEnabled(eventJson)}");
                            return;
                        }

                        onNativePaymentEvent?.Invoke(paymentEvent, null);

                        XsollaLogger.Debug(TAG, $"Received payment event (event={paymentEvent}): {LogObfuscation.ObfuscateJsonIfEnabled(eventJson)}");
                    },
                    onError: _ => {
                        // Not supported.
                    }
                );
            }
        }

        #endregion

        #region Restore

        public void RestorePurchases(RestorePurchasesResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "Restore");
            
            var initData = m_InitData;
            if (initData == null)
            {
                XsollaClientBridgeHelpersAndroid.ReportError(Tag, "Is not initialized");
                onError?.Invoke("Is not initialized");
                return;
            }
            
            initData.initializeFuture.OnComplete(
                onSuccess: _ =>
                {
                    var restore = initData.restoredTransactionIds.OnRestoreSent();

                    XsollaClientBridgeHelpersAndroid.JavaCall(
                        method: "Restore",
                        json: () => XsollaStoreClientHelpers.EmptyJson,
                        logTag: "Restore",
                        onSuccess: result => {
                            if (initData != m_InitData) {
                                // Its ids and receipts belong to a session a re-Initialize or Deinitialize ended, and
                                // would land in the cache of the current one.
                                XsollaLogger.Debug(Tag, "Restore answered after the session it was sent in ended, dropped");
                                onError?.Invoke("Restore answered after the session it was sent in ended");
                            } else {
                                // Native makes up an inventory unit's ids from its position, which the next unit takes
                                // over once it is consumed, so Unity IAP 4.x would finish that unit without granting it.
                                var products = initData.restoredTransactionIds.Assign(
                                    restore,
                                    XsollaStoreClientHelpers.JsonToRestoredItems(result)
                                );

                                foreach (var product in products) {
                                    AddReceiptToCache(product.transactionId, product.sku, product.receipt, product.quantity);
                                }

                                onSuccess?.Invoke(products);
                            }
                        },
                        onError: error => {
                            initData.restoredTransactionIds.OnRestoreFailed(restore);
                            onError?.Invoke(error);
                        }
                    );
                },
                onError: error => onError?.Invoke(error)
            );
        }

        #endregion

        #region ProductsRequest

        public void FetchProducts(string[] productIds, FetchProductsResultFunc onSuccess, ErrorFunc onError)
        {
            if (m_InitData == null)
            {
                XsollaClientBridgeHelpersAndroid.ReportError(Tag, "Is not initialized");
                onError?.Invoke("Is not initialized");
                return;
            }
            
            m_InitData.initializeFuture.OnComplete(
                onSuccess: _ =>
                {
                    XsollaClientBridgeHelpersAndroid.JavaCall(
                        method: "ProductsRequest",
                        json: () => XsollaStoreClientHelpers.ProductIdsToJson(productIds),
                        logTag: "ProductsRequest",
                        onSuccess: result => onSuccess?.Invoke(XsollaStoreClientHelpers.JsonToProducts(result)),
                        onError: error => onError?.Invoke(error)
                    );
                },
                onError: error => onError?.Invoke(error)
            );
        }

        #endregion

        #region Purchase

        public void PurchaseProduct(string sku, string developerPayload, XsollaStoreClientPurchaseArgs args, PurchaseProductResultFunc onSuccess, ErrorFunc onError)
        {
            var initData = m_InitData;
            if (initData == null)
            {
                XsollaClientBridgeHelpersAndroid.ReportError(Tag, "Payment listener is not initialized");
                onError?.Invoke(XsollaClientHelpers.ToJson(
                    new XsollaStoreClientError("Payment listener is not initialized", XsollaStoreClientPurchaseErrorCode.Unknown)
                    {
                        reason = XsollaPurchasingFailureReason.PurchasingUnavailable
                    }
                ));
                return;
            }

            var finalDeveloperPayload = args.developerPayload ?? developerPayload;

            var purchaseCallback = XsollaClientBridgeHelpersAndroid.CreateCommonListenerCallback(
                "Purchase",
                onSuccess: s => {
                    var product = XsollaStoreClientHelpers.JsonToPurchase(s, finalDeveloperPayload);
                    AddReceiptToCache(product.transactionId, product.sku, product.receipt, restoredQuantity: 0);
                    onSuccess?.Invoke(product);
                },
                onError: error => {
                    onError?.Invoke(error);
                }
            );

            Action<string, string> answer = (result, error) => purchaseCallback(result, error);

            // The Android SDK reports no web shop purchase, so a paid one arrives with the next restore, and only its start
            // failing answers its request: the call below throwing, or the bridge reporting a failure naming its SKU.
            var request = initData.IsWebShopPurchase(args)
                ? initData.purchaseRequests.AddWithoutResult(sku, answer)
                : initData.purchaseRequests.Add(sku, args.paymentToken, answer);
            
            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "Purchase",
                onError: error => {
                    // The purchase never started, so no payment result belongs to it. One that answered it meanwhile
                    // was its only answer.
                    if (initData.purchaseRequests.Remove(request)) {
                        var msg = $"Purchase failed: {error}";
                        XsollaClientBridgeHelpersAndroid.ReportError(Tag, msg);
                        // The bridge throws before any UI, such as when its billing client is not ready.
                        onError?.Invoke(XsollaClientHelpers.ToJson(
                            new XsollaStoreClientError(error, XsollaStoreClientPurchaseErrorCode.Unknown)
                            {
                                reason = XsollaPurchasingFailureReason.PurchasingUnavailable
                            }
                        ));
                    } else {
                        XsollaLogger.Warning(Tag, $"Purchase failed to start after a payment result answered it: {error}");
                    }
                },
                args: () => new object[] {
                    XsollaStoreClientHelpers.PurchaseToJson(sku,
                        finalDeveloperPayload,
                        args.externalId, args.paymentToken,
                        args.paymentMethodId,
#pragma warning disable CS0618 // Type or member is obsolete
                        args.allowTokenOnlyFinishedStatusWithoutOrderId,
#pragma warning restore CS0618 // Type or member is obsolete
                        args.externalTransactionToken
                    )
                }
            );
        }

        /// <summary>
        /// False for a web shop purchase, which only its start failure answers; a purchase before initialization is
        /// answered at once with an error.
        /// </summary>
        public bool CanAnswerPurchase(string sku, XsollaStoreClientPurchaseArgs args)
        {
            var initData = m_InitData;
            return initData == null || !initData.IsWebShopPurchase(args);
        }

        #endregion

        #region Consume

        public void ConsumeProduct(string sku, int quantity, string transactionId, ConsumeProductResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, $"[XsollaStoreClientImplAndroid] Consume product (sku={sku} quantity={quantity} transactionId={transactionId}");

            if (m_InitData?.receiptCache == null) {
                XsollaLogger.Debug(Tag, "Receipt cache doesn't exist, populating..");

                RestorePurchases(
                    onSuccess: _ => performConsumption(),
                    onError: err => {
                        XsollaClientBridgeHelpersAndroid.ReportError(Tag,
                            $"[ConsumeProduct] Failed to cache unconsumed purchase receipts:\n{err}"
                        );
                        performConsumption();
                    }
                );
            } else {
                performConsumption();
            }

            void performConsumption() {
                // Read the live field rather than closing over one captured above: when the branch above found
                // it `null` it repopulated it through `RestorePurchases` -> `AddReceiptToCache`, which assigns
                // `m_InitData.receiptCache`. A captured local would still be `null` here and the populate path
                // would always error out. An empty cache answers the purchase as unknown.
                (m_InitData?.receiptCache ?? new XsollaReceiptCacheAndroid()).Consume(
                    transactionId,
                    sku,
                    quantity,
                    consumeWithToken: receipt => XsollaClientBridgeHelpersAndroid.JavaCall(
                        method: "Consume",
                        json: () => XsollaStoreClientHelpers.ConsumeToJson(sku, quantity, transactionId, receipt),
                        logTag: "Consume",
                        onSuccess: _ => {
                            markConsumed();
                            onSuccess.Invoke();
                        },
                        onError: onConsumeError
                    ),
                    onSuccess: () => onSuccess.Invoke(),
                    onError: error => onError?.Invoke(error)
                );
            }

            void onConsumeError(string error) {
                var verdict = XsollaConsumeOutcomeCheckAndroid.Evaluate(error, out var facts);
                var outcome = facts == null
                    ? ""
                    : $" (quantity={facts.Quantity} skuQuantityBefore={facts.SkuQuantityBefore?.ToString() ?? "n/a"} " +
                      $"skuQuantityAfter={facts.SkuQuantityAfter?.ToString() ?? "n/a"} " +
                      $"otherConsumeOverlapped={facts.IsOtherConsumeOverlapped} purchaseOverlapped={facts.IsPurchaseOverlapped})";

                if (verdict == XsollaConsumeOutcomeCheckAndroid.Verdict.Consumed) {
                    XsollaLogger.Warning(Tag, $"[ConsumeProduct] The consume outcome was unknown, the inventory shows it landed{outcome} (sku={sku} transactionId={transactionId})");
                    markConsumed();
                    onSuccess.Invoke();
                } else if (verdict == XsollaConsumeOutcomeCheckAndroid.Verdict.NotConsumed) {
                    onError?.Invoke($"Consumption failed, the outcome was unknown and the inventory shows it did not land{outcome}: {facts?.Message}");
                } else if (verdict == XsollaConsumeOutcomeCheckAndroid.Verdict.Unresolved) {
                    m_InitData?.receiptCache?.MarkRefused(transactionId, sku);
                    XsollaClientBridgeHelpersAndroid.ReportError(Tag,
                        $"[ConsumeProduct] The consume outcome is unknown and could not be resolved{outcome}, refusing further consumes of the purchase this session (sku={sku} transactionId={transactionId})"
                    );
                    onError?.Invoke($"Consumption failed with an unknown outcome{outcome}, refusing further consumes of the purchase this session: {facts?.Message ?? error}");
                } else {
                    onError?.Invoke(facts?.Message ?? error);
                }
            }

            void markConsumed() {
                var liveInitData = m_InitData;
                if (liveInitData?.receiptCache != null) {
                    liveInitData.receiptCache.MarkConsumed(transactionId, sku);
                    liveInitData.restoredTransactionIds.OnConsumed(sku, transactionId);
                } else {
                    XsollaLogger.Debug(Tag, "[ConsumeProduct] Tried to mark the receipt " +
                        $"cache consumed on successful consumption, but it doesn't exist (sku={sku} " +
                        $"transactionId={transactionId})"
                    );
                }
            }
        }
        
        #endregion

        #region Validate

        public void ValidatePurchase(string receiptJson, ValidatePurchaseResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "Validate",
                json: () => receiptJson,
                logTag: "Validate",
                onSuccess: json => onSuccess?.Invoke(XsollaStoreClientHelpers.JsonToValidate(json)),
                onError: error => onError?.Invoke(error)
            );
        }

        #endregion

        #region Deinitialize

        public void Deinitialize(DeinitializeResultFunc onSuccess, ErrorFunc onError)
        {
            instances.Remove(this);

            var endedInitData = m_InitData;
            m_InitData = null;
            EndSession(endedInitData, "the store client was deinitialized");

            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "Deinitialize",
                json: () => XsollaStoreClientHelpers.EmptyJson,
                logTag: "Deinitialize",
                onSuccess: _ => onSuccess?.Invoke(),
                onError: error => onError?.Invoke(error)
            );
       }

        #endregion
        
        #region Token
        public void GetAccessToken(GetAccessTokenResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "GetAccessToken",
                json: () => XsollaStoreClientHelpers.EmptyJson,
                logTag: "GetAccessToken",
                onSuccess: json => onSuccess?.Invoke(XsollaStoreClientHelpers.JsonToToken(json)),
                onError: error => onError?.Invoke(error)
            );
        }

        public void UpdateAccessToken(string token, UpdateAccessTokenResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "UpdateAccessToken",
                json: () => token,
                logTag: "UpdateAccessToken",
                onSuccess: _ => onSuccess?.Invoke(),
                onError: error => onError?.Invoke(error)
            );
        }

        #endregion
        
        #region unsupported
        public void GetAppleStorefront(GetAppleStorefrontResultFunc onSuccess, ErrorFunc onError)
        {
            onError?.Invoke("unsupported platform");
        }
        #endregion

        public static void CancelActivePurchase()
        {
            foreach (var instance in instances) {
                instance.CancelActivePurchase_();
            }
        }

        private void AddReceiptToCache(string transactionId, string sku, string receipt, int restoredQuantity)
        {
            if (m_InitData == null) {
                XsollaClientBridgeHelpersAndroid.ReportError(Tag,
                    "Failed to add a receipt to cache because it's not initialized"
                );
                return;
            }

            (m_InitData.receiptCache ??= new XsollaReceiptCacheAndroid()).Add(transactionId, sku, receipt, restoredQuantity);
        }

        private void CancelActivePurchase_()
        {
            XsollaLogger.Debug(Tag, "CancelActivePurchase");

            if (m_InitData == null) {
                XsollaClientBridgeHelpersAndroid.ReportError(Tag,
                    "Failed to cancel an active purchase because store is not initialized"
                );
                return;
            }

            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "CancelActivePurchase",
                onError: null,
                args: () => new object[] { XsollaStoreClientHelpers.EmptyJson, XsollaClientBridgeHelpersAndroid.Dummy }
            );
        }
    }
}
#endif
