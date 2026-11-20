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
            [NotNull] public readonly ISimpleFuture<bool, string> initializeFuture;

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
                [NotNull] ISimpleFuture<bool, string> initializeFuture
            ) {
                this.stateListener = stateListener;
                this.paymentListener = paymentListener;
                this.initializeFuture = initializeFuture;
            }
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

            var stateListener = XsollaClientBridgeHelpersAndroid.CreateCommonListener("State Listener", (result, error) => {
                XsollaLogger.Debug(Tag, $"State Listener: {LogObfuscation.ObfuscateJsonIfEnabled(result)} {error}");
            });

            var paymentListener = XsollaClientBridgeHelpersAndroid.CreateCommonListener("Payment Listener" ,(result, error) => {
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
            });
            
            var initializeFuture = SimpleFuture.Create<bool, string>(out var promise);

            m_InitData = new InitData(
                stateListener,
                paymentListener,
                initializeFuture
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

            var additionalSettings = AdditionalSettings.FromExtensions();

            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "Initialize",
                onError: error => onError?.Invoke(error),
                XsollaClientHelpers.ConfigurationToJson(configuration),
                XsollaClientHelpers.ToJson(additionalSettings),
                stateListener,
                paymentListener,
                TryCreatePaymentEventCallback()
            );

            instances.Add(this);


            XsollaClientBridgeHelpersAndroid.XsollaUnityBridgeJsonCallback TryCreatePaymentEventCallback()
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
                        json: XsollaStoreClientHelpers.EmptyJson,
                        callback: XsollaClientBridgeHelpersAndroid.CreateCallback( "Restore",
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
                        )
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
                        json: XsollaStoreClientHelpers.ProductIdsToJson(productIds),
                        callback: XsollaClientBridgeHelpersAndroid.CreateCallback("ProductsRequest",
                            onSuccess: result => onSuccess?.Invoke(XsollaStoreClientHelpers.JsonToProducts(result)),
                            onError: error => onError?.Invoke(error)
                        )
                    );
                },
                onError: error => onError?.Invoke(error)
            );
        }

        #endregion

        #region Purchase

        public void PurchaseProduct(string sku, string developerPayload, XsollaStoreClientPurchaseArgs args, PurchaseProductResultFunc onSuccess, ErrorFunc onError)
        {
            var paymentListener = m_InitData?.paymentListener;
            if (paymentListener == null)
            {
                XsollaClientBridgeHelpersAndroid.ReportError(Tag, "Payment listener is not initialized");
                onError?.Invoke("Payment listener is not initialized");
                return;
            }

            var finalDeveloperPayload = args.developerPayload ?? developerPayload;

            XsollaClientBridgeHelpersAndroid.AddCommonListenerCallback(
                paymentListener,
                "Purchase",
                onSuccess: s => {
                    var product = XsollaStoreClientHelpers.JsonToPurchase(s, finalDeveloperPayload);
                    AddReceiptToCache(product.transactionId, product.sku, product.receipt, restoredQuantity: 0);
                    onSuccess?.Invoke(product);
                },
                onError: error => {
                    onError?.Invoke(error);
                },
                onValidate: (result, error) => {
                    XsollaLogger.Debug(Tag, $"Validate: {LogObfuscation.ObfuscateJsonIfEnabled(result)} {error}");
                    return true;
                }
            );
            
            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "Purchase",
                onError: error => {
                    var msg = $"Purchase failed: {error}";
                    XsollaClientBridgeHelpersAndroid.ReportError(Tag, msg);
                    onError?.Invoke(error);
                },
                XsollaStoreClientHelpers.PurchaseToJson(sku,
                    finalDeveloperPayload,
                    args.externalId, args.paymentToken,
                    args.paymentMethodId,
#pragma warning disable CS0618 // Type or member is obsolete
                    args.allowTokenOnlyFinishedStatusWithoutOrderId,
#pragma warning restore CS0618 // Type or member is obsolete
                    args.externalTransactionToken
                )
            );
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
                        json: XsollaStoreClientHelpers.ConsumeToJson(sku, quantity, transactionId, receipt),
                        callback: XsollaClientBridgeHelpersAndroid.CreateCallback( "Consume",
                            onSuccess: _ => {
                                markConsumed();
                                onSuccess.Invoke();
                            },
                            onError: onConsumeError
                        )
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
                json: receiptJson,
                callback: XsollaClientBridgeHelpersAndroid.CreateCallback( "Validate",
                    onSuccess: json => onSuccess?.Invoke(XsollaStoreClientHelpers.JsonToValidate(json)),
                    onError: error => onError?.Invoke(error)
                )
            );
        }

        #endregion

        #region Deinitialize

        public void Deinitialize(DeinitializeResultFunc onSuccess, ErrorFunc onError)
        {
            instances.Remove(this);

            m_InitData = null;

            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "Deinitialize",
                json: XsollaStoreClientHelpers.EmptyJson,
                callback: XsollaClientBridgeHelpersAndroid.CreateCallback( "Deinitialize",
                    onSuccess: _ => onSuccess.Invoke(),
                    onError: error => onError?.Invoke(error)
                )
            );
       }

        #endregion
        
        #region Token
        public void GetAccessToken(GetAccessTokenResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "GetAccessToken",
                json: XsollaStoreClientHelpers.EmptyJson,
                callback: XsollaClientBridgeHelpersAndroid.CreateCallback( "GetAccessToken",
                    onSuccess: json => onSuccess?.Invoke(XsollaStoreClientHelpers.JsonToToken(json)),
                    onError: error => onError?.Invoke(error)
                )
            );
        }

        public void UpdateAccessToken(string token, UpdateAccessTokenResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaClientBridgeHelpersAndroid.JavaCall(
                method: "UpdateAccessToken",
                json: token,
                callback: XsollaClientBridgeHelpersAndroid.CreateCallback( "UpdateAccessToken",
                    onSuccess: _ => onSuccess?.Invoke(),
                    onError: error => onError?.Invoke(error)
                )
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
                json: XsollaStoreClientHelpers.EmptyJson,
                callback: XsollaClientBridgeHelpersAndroid.Dummy
            );
        }
    }
}
#endif
