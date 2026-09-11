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
            /// Purchase token per (transaction, sku), as a <see cref="ReceiptEntry"/> — see that type for the
            /// state model.
            /// <para/>
            /// Keyed by sku as well, not by transaction id alone: bundle and virtual-currency purchases are
            /// delivered as several content lines that share one transaction id but each carry a distinct
            /// receipt, so a transaction-only key lets one line's token overwrite another's.
            /// </summary>
            [CanBeNull] public Dictionary<(string transactionId, string sku), ReceiptEntry> receiptCache;

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

        /// <summary>
        /// Cache value for one purchased content line. An explicit two-state model that replaces a nullable
        /// receipt string whose <c>null</c>/<c>""</c>/token meanings had to be reverse-engineered from the
        /// consume code. A key absent from the cache means the purchase is unrecognized.
        /// </summary>
        private abstract class ReceiptEntry
        {
            /// <summary>
            /// Awaiting a consume, carrying the purchase token to consume with. The token may be empty for a
            /// free or zero-price line.
            /// </summary>
            public sealed class Pending : ReceiptEntry
            {
                public readonly string Receipt;

                public Pending(string receipt)
                {
                    Receipt = receipt ?? "";
                }
            }

            /// <summary>
            /// Already consumed this session, so a consume repeated for it resolves as an idempotent success
            /// rather than spending a token the native bridge has already consumed.
            /// </summary>
            public sealed class Consumed : ReceiptEntry
            {
                public static readonly Consumed Instance = new();

                private Consumed() { }
            }

            private ReceiptEntry() { }
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
                XsollaLogger.Debug(Tag, $"State Listener: {result} {error}");
            });

            var paymentListener = XsollaClientBridgeHelpersAndroid.CreateCommonListener("Payment Listener" ,(result, error) => {
                XsollaLogger.Debug(Tag, $"Payment Listener: common callback fire result={result} error={error}");

                if (onSuccessPurchaseProduct != null || onErrorPurchase != null)
                {
                    RunOnStartThread.Run(() => {
                        if (result != null)
                        {
                            XsollaLogger.Debug(Tag, $"Payment Listener: result={result}");
                            onSuccessPurchaseProduct?.Invoke(XsollaStoreClientHelpers.JsonToPurchase(result, null));
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
                            XsollaLogger.Warning(TAG, $"Failed to parse native payment event: {eventJson}");
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
                            XsollaLogger.Warning(TAG, $"Unsupported payment event type: {nativePaymentEvent.type}, {eventJson}");
                            return;
                        }

                        onNativePaymentEvent?.Invoke(paymentEvent, null);

                        XsollaLogger.Debug(TAG, $"Received payment event (event={paymentEvent}): {eventJson}");
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
                        method: "Restore",
                        json: XsollaStoreClientHelpers.EmptyJson,
                        callback: XsollaClientBridgeHelpersAndroid.CreateCallback( "Restore",
                            onSuccess: result => {
                                var products = XsollaStoreClientHelpers.JsonToRestoredItems(result);

                                foreach (var product in products) {
                                    AddReceiptToCache(product.transactionId, product.sku, product.receipt);
                                }

                                onSuccess?.Invoke(products);
                            },
                            onError: error => onError?.Invoke(error)
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
                    AddReceiptToCache(product.transactionId, product.sku, product.receipt);
                    onSuccess?.Invoke(product);
                },
                onError: error => {
                    onError?.Invoke(error);
                },
                onValidate: (result, error) => {
                    XsollaLogger.Debug(Tag, $"Validate: {result} {error}");
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
                // would always error out.
                var receiptCache = m_InitData?.receiptCache;
                if (receiptCache == null || !receiptCache.TryGetValue((transactionId, sku), out var entry)) {
                    XsollaLogger.Debug(Tag, $"[ConsumeProduct] Unrecognized purchase: transactionId={transactionId} sku={sku}");
                    onError?.Invoke($"Consumption failed due to the unknown purchase: transactionId={transactionId} sku={sku}");
                } else if (entry is ReceiptEntry.Consumed) {
                    // Unity IAP finishes the same transaction twice (`ProcessPurchaseOnStart` re-finishes every
                    // already-logged transaction on each `OnProductsRetrieved`); the second finish would
                    // otherwise fire a tokenless consume and be rejected by the native bridge.
                    XsollaLogger.Debug(Tag, $"[ConsumeProduct] Already consumed this session, ignoring duplicate (sku={sku} transactionId={transactionId}).");
                    onSuccess.Invoke();
                } else if (entry is ReceiptEntry.Pending pending) {
                    XsollaClientBridgeHelpersAndroid.JavaCall(
                        method: "Consume",
                        json: XsollaStoreClientHelpers.ConsumeToJson(sku, quantity, transactionId, pending.Receipt),
                        callback: XsollaClientBridgeHelpersAndroid.CreateCallback( "Consume",
                            onSuccess: _ => {
                                var liveReceiptCache = m_InitData?.receiptCache;
                                if (liveReceiptCache != null) {
                                    liveReceiptCache[(transactionId, sku)] = ReceiptEntry.Consumed.Instance;
                                } else {
                                    XsollaLogger.Debug(Tag, "[ConsumeProduct] Tried to mark the receipt " +
                                        $"cache consumed on successful consumption, but it doesn't exist (sku={sku} " +
                                        $"transactionId={transactionId})"
                                    );
                                }

                                onSuccess.Invoke();
                            },
                            onError: error => onError?.Invoke(error)
                        )
                    );
                } else {
                    // Unreachable: `ReceiptEntry` is a closed `Pending`|`Consumed` hierarchy. Defensive branch so
                    // a future entry kind cannot silently fall through to a no-op that strands the transaction.
                    XsollaClientBridgeHelpersAndroid.ReportError(Tag,
                        $"[ConsumeProduct] Unexpected receipt entry {entry.GetType().Name} (sku={sku} transactionId={transactionId})"
                    );
                    onError?.Invoke($"Consumption failed due to an unexpected receipt entry (sku={sku} transactionId={transactionId})");
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

        private void AddReceiptToCache(string transactionId, string sku, string receipt)
        {
            if (m_InitData == null) {
                XsollaClientBridgeHelpersAndroid.ReportError(Tag,
                    "Failed to add a receipt to cache because it's not initialized"
                );
                return;
            }

            var receiptCache =
                m_InitData.receiptCache ??= new Dictionary<(string transactionId, string sku), ReceiptEntry>(capacity: 4);

            var key = (transactionId, sku);

            // Only warn when overwriting a still-`Pending` purchase whose non-empty token differs — a `Consumed`
            // entry being re-armed by a fresh delivery is expected, not a conflict.
            if (receiptCache.TryGetValue(key, out var existing) &&
                existing is ReceiptEntry.Pending existingPending &&
                !string.IsNullOrEmpty(existingPending.Receipt) &&
                existingPending.Receipt != receipt) {
                XsollaLogger.Warning(Tag, "Cache already had a receipt assigned to a " +
                    $"purchase (transactionId={transactionId} sku={sku} newReceipt={receipt} " +
                    $"oldReceipt={existingPending.Receipt})"
                );
            }

            receiptCache[key] = new ReceiptEntry.Pending(receipt);
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
