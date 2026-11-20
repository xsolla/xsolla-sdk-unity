#if UNITY_IOS || UNITY_EDITOR

using System;
using System.Runtime.InteropServices;
using AOT;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using Xsolla.SDK.Common;
using Xsolla.SDK.Common.Extensions;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Store
{
    internal class XsollaStoreClientImplIOS : IXsollaStoreClient
    {
        private const string Tag = "XsollaStoreClientImplIOS";
        
        #region Callbacks
        
        [CanBeNull] private XsollaClientCallback m_PaymentListener;

        /// <summary>
        /// Tells where each delivered purchase came from, under the settings of the last <see cref="Initialize"/>.
        /// </summary>
        [CanBeNull] private XsollaPurchaseOriginsIOS m_PurchaseOrigins;

        public static event Action<string, object> onNativePaymentEvent;
        
        [Serializable]
        public class NativePaymentEvent
        {
            /// <summary>
            /// Must match the iOS native implementation.
            /// </summary>
            public enum Type
            {
                WillOpen,
                Opened,
                Loaded,
                WillClose,
                Closed,
                OpenedExternally,
                Completed
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

        #endregion


        #region Native logging

        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeSetLogCallback(XsollaUnityBridgeLogCallbackDelegate callback);

        /// <summary>
        /// <paramref name="level"/> is an <see cref="XsollaLogLevel"/> member name, as the native
        /// side spells it.
        /// </summary>
        private delegate void XsollaUnityBridgeLogCallbackDelegate(string level, string message);

        /// <summary>
        /// Kept in a field so the delegate isn't collected while the native side holds the pointer.
        /// </summary>
        private static readonly XsollaUnityBridgeLogCallbackDelegate _nativeLogCallback = OnNativeLog;

        private const string NativeTag = "iOS SDK";

        /// <summary>
        /// Puts a line the iOS SDK logged into the Unity log, where a developer without Xcode
        /// attached can read it. Only warnings and errors are forwarded. With secrets obfuscation on, the values of
        /// token and secret JSON keys in the line are obfuscated, as on Android; a secret outside JSON is not.
        /// </summary>
        [MonoPInvokeCallback(typeof(XsollaUnityBridgeLogCallbackDelegate))]
        private static void OnNativeLog(string level, string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            var obfuscatedMessage = LogObfuscation.ObfuscateJsonIfEnabled(message);
            if (string.Equals(level, nameof(XsollaLogLevel.Error), StringComparison.OrdinalIgnoreCase))
                XsollaLogger.Error(NativeTag, obfuscatedMessage);
            else
                XsollaLogger.Warning(NativeTag, obfuscatedMessage);
        }

        #endregion


        #region Initialization
        
        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeSetupAnalytics(string version);

        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeInitialize(string configJson, string additionalSettingsJson, XsollaClientBridgeHelpersIOS.XsollaUnityBridgeJsonCallbackDelegate callback, Int64 callbackData, Int64 paymentListener, XsollaClientBridgeHelpersIOS.XsollaUnityBridgeJsonCallbackDelegate paymentEventCallback, Int64 paymentEventCallbackData);

        [Serializable]
        public class AdditionalSettings
        {
            [CanBeNull] public RetryPolicies retryPolicies;

            [CanBeNull]
            public static AdditionalSettings FromExtensions()
            {
                var extensionsHandler = XsollaStoreClientExtensionsProvider.Handler;
                if (extensionsHandler == null)
                    return null;

                return new AdditionalSettings
                {
                    retryPolicies = extensionsHandler.GetRetryPolicies()
                };
            }
        }

        public void Initialize(XsollaClientConfiguration configuration, 
            InitializeResultFunc onSuccess, ErrorFunc onError,
            [CanBeNull] PurchaseProductResultFunc onSuccessPurchaseProduct, [CanBeNull] ErrorFunc onErrorPurchase
        ) {
            XsollaLogger.Debug(Tag, $"Initialize: {configuration}");
            
            RunOnStartThread.Create();

            var purchaseOrigins = new XsollaPurchaseOriginsIOS(configuration.settings.webhooksMode, configuration.settings.localPurchasesRestore);
            m_PurchaseOrigins = purchaseOrigins;
            
            m_PaymentListener = XsollaClientBridgeHelpersIOS.CreateCommonListener("Payment Listener",(result, error) => {
                XsollaLogger.Debug(Tag, $"Payment Listener: common callback fire result={LogObfuscation.ObfuscateJsonIfEnabled(result)} error={error}");

                if (onSuccessPurchaseProduct != null || onErrorPurchase != null)
                {
                    RunOnStartThread.Run(() => {
                        if (result != null)
                        {
                            XsollaLogger.Debug(Tag, $"Payment Listener: result={LogObfuscation.ObfuscateJsonIfEnabled(result)}");
                            var product = XsollaStoreClientHelpers.JsonToPurchase(result, null);
                            product.origin = purchaseOrigins.OfListenerPurchase(result);
                            onSuccessPurchaseProduct?.Invoke(product);
                        }
                        else
                        {
                            XsollaLogger.Error(Tag, $"Payment Listener: failed={error}");
                            onErrorPurchase?.Invoke(error ?? string.Empty);
                        }
                    });
                }
            });

            // Before anything else, so a warning or error from setup itself lands in the Unity log.
            _XsollaUnityBridgeSetLogCallback(_nativeLogCallback);

            _XsollaUnityBridgeSetupAnalytics(Application.unityVersion); // will init Unity related analytics and setup its version

            var additionalSettings = AdditionalSettings.FromExtensions();

            var paymentEventCallback = TryCreatePaymentEventCallback();
            
            _XsollaUnityBridgeInitialize(
                configJson: XsollaClientHelpers.ConfigurationToJson(configuration),
                additionalSettingsJson: XsollaClientHelpers.ToJson(additionalSettings),
                callback: XsollaClientBridgeHelpersIOS.OnXsollaUnityBridgeJsonCallback,
                callbackData: XsollaClientBridgeHelpersIOS.CreateCallbackData( "Initialize",
                    onSuccess: _ => onSuccess?.Invoke(),
                    onError: error => onError?.Invoke(error)
                ),
                paymentListener: m_PaymentListener.callbackId,
                paymentEventCallback: paymentEventCallback != null ? XsollaClientBridgeHelpersIOS.OnXsollaUnityBridgeJsonCallback : null,
                paymentEventCallbackData: paymentEventCallback?.callbackId ?? 0
            );
        }

        [CanBeNull]
        private XsollaClientCallback TryCreatePaymentEventCallback()
        {
            const string TAG = "PaymentEventCallback";

            return XsollaClientBridgeHelpersIOS.CreateCommonListener(TAG, (result, error) => {
                if (error != null)
                {
                    // Not supported for errors
                    return;
                }

                var nativePaymentEvent = NativePaymentEvent.FromJson(result);
                if (nativePaymentEvent == null)
                {
                    XsollaLogger.Warning(TAG, $"Failed to parse native payment event: {LogObfuscation.ObfuscateJsonIfEnabled(result)}");
                    return;
                }

                var paymentEvent = nativePaymentEvent.type switch
                {
                    NativePaymentEvent.Type.Opened => "PaystationOpen",
                    NativePaymentEvent.Type.Loaded => "PaystationLoaded",
                    NativePaymentEvent.Type.Closed => "PaystationCancelled",
                    NativePaymentEvent.Type.Completed => "PaystationCompleted",
                    _ => null
                };

                if (paymentEvent == null)
                {
                    XsollaLogger.Debug(TAG, $"Unmapped payment event type: {nativePaymentEvent.type}, {LogObfuscation.ObfuscateJsonIfEnabled(result)}");
                    return;
                }

                onNativePaymentEvent?.Invoke(paymentEvent, null);

                XsollaLogger.Debug(TAG, $"Received payment event (event={paymentEvent}): {LogObfuscation.ObfuscateJsonIfEnabled(result)}");
            });
        }
        
        #endregion
        
        #region Restore
        
        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeRestore(XsollaClientBridgeHelpersIOS.XsollaUnityBridgeJsonCallbackDelegate callback, Int64 callbackData);
        public void RestorePurchases(RestorePurchasesResultFunc onSuccess, ErrorFunc onError)
        {
            _XsollaUnityBridgeRestore(
                callback: XsollaClientBridgeHelpersIOS.OnXsollaUnityBridgeJsonCallback,
                callbackData: XsollaClientBridgeHelpersIOS.CreateCallbackData("Restore",
                    onSuccess: result => onSuccess?.Invoke(XsollaStoreClientHelpers.JsonToRestoredItems(result)),
                    onError: error => onError?.Invoke(error)
                )
            );
        }
        
        #endregion

        #region ProductsRequest
        
        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeProductsRequest(string productIdsJson, XsollaClientBridgeHelpersIOS.XsollaUnityBridgeJsonCallbackDelegate callback, Int64 callbackData);
        public void FetchProducts(string[] productIds, FetchProductsResultFunc onSuccess, ErrorFunc onError)
        {
            _XsollaUnityBridgeProductsRequest(
                productIdsJson: XsollaStoreClientHelpers.ProductIdsToJson(productIds),
                callback: XsollaClientBridgeHelpersIOS.OnXsollaUnityBridgeJsonCallback, 
                callbackData: XsollaClientBridgeHelpersIOS.CreateCallbackData("ProductsRequest",
                    onSuccess: result => onSuccess?.Invoke(XsollaStoreClientHelpers.JsonToProducts(result)),
                    onError: error => onError?.Invoke(error)
                )
            );
        }
        
        #endregion

        #region Purchase
        
        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgePurchase(string sku, string developerPayload, string externalId, int paymentMethod, string paymentToken, bool allowTokenOnlyFinishedStatusWithoutOrderId, XsollaClientBridgeHelpersIOS.XsollaUnityBridgeJsonCallbackDelegate callback, Int64 callbackData);
        public void PurchaseProduct(string sku, string developerPayload, XsollaStoreClientPurchaseArgs args, PurchaseProductResultFunc onSuccess, ErrorFunc onError)
        {
	        var finalDeveloperPayload = developerPayload ?? args.developerPayload;

            // TODO: forward `args.externalTransactionToken` to the native bridge. Both
            // `_XsollaUnityBridgePurchase` and its Objective-C counterpart need the extra argument
            // before the token can be applied here; until then it is reported as unsupported.
            if (!string.IsNullOrEmpty(args.externalTransactionToken))
            {
                onError?.Invoke("External transaction token is not supported on iOS yet");
                return;
            }

            _XsollaUnityBridgePurchase(
                sku, finalDeveloperPayload, args.externalId, args.paymentMethodId ?? -1, args.paymentToken, args.allowTokenOnlyFinishedStatusWithoutOrderId,
                callback: XsollaClientBridgeHelpersIOS.OnXsollaUnityBridgeJsonCallback, 
                callbackData: XsollaClientBridgeHelpersIOS.CreateCallbackData("Purchase",
                    onSuccess: result =>
                    {
                        var product = XsollaStoreClientHelpers.JsonToPurchase(result, finalDeveloperPayload);
                        product.origin = m_PurchaseOrigins?.OfPurchaseCallAnswer(result) ?? XsollaStoreClientPurchasedProduct.Origin.Unknown;
                        onSuccess?.Invoke(product);
                    },
                    onError: error => onError?.Invoke(error)
                )
            );
        }
        
        #endregion

        #region Consume
        
        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeConsume(string sku, int quantity, XsollaClientBridgeHelpersIOS.XsollaUnityBridgeJsonCallbackDelegate callback, Int64 callbackData);
        public void ConsumeProduct(string sku, int quantity, string transactionId, ConsumeProductResultFunc onSuccess, ErrorFunc onError)
        {
            _XsollaUnityBridgeConsume(
                sku,
                quantity,
                callback: XsollaClientBridgeHelpersIOS.OnXsollaUnityBridgeJsonCallback, 
                callbackData: XsollaClientBridgeHelpersIOS.CreateCallbackData("Consume",
                    onSuccess: _ => onSuccess?.Invoke(),
                    onError: error => onError?.Invoke(error)
                )
            );
        }
        #endregion

        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeValidate(string transactionId, string productId, XsollaClientBridgeHelpersIOS.XsollaUnityBridgeJsonCallbackDelegate callback, Int64 callbackData);
        public void ValidatePurchase(string receipt, ValidatePurchaseResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, $"ValidatePurchase: {LogObfuscation.ObfuscateJsonIfEnabled(receipt)}");
            
            var r = XsollaStoreClientHelpers.JsonToReceipt(receipt);
            if (r == null)
            {
                onError?.Invoke("Invalid receipt");
                return;
            }
            
            if (r.orderStatus == XsollaStoreClientPurchasedProduct.Status.Restored)
            {
                onSuccess?.Invoke(true);
                return;
            }
            
            _XsollaUnityBridgeValidate(
                r.transactionId, r.productId,
                callback: XsollaClientBridgeHelpersIOS.OnXsollaUnityBridgeJsonCallback, 
                callbackData: XsollaClientBridgeHelpersIOS.CreateCallbackData("Validate",
                    onSuccess: result => onSuccess?.Invoke(XsollaStoreClientHelpers.JsonToValidate(result)),
                    onError: error => onError?.Invoke(error)
                )
            );
        }
        
        #region Deinitialize
        
        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeDeinitialize(XsollaClientBridgeHelpersIOS.XsollaUnityBridgeJsonCallbackDelegate callback, Int64 callbackData);
        public void Deinitialize(DeinitializeResultFunc onSuccess, ErrorFunc onError)
        {
            _XsollaUnityBridgeDeinitialize(
                callback: XsollaClientBridgeHelpersIOS.OnXsollaUnityBridgeJsonCallback, 
                callbackData: XsollaClientBridgeHelpersIOS.CreateCallbackData("Deinitialize",
                    onSuccess: _ =>
                    {
                        XsollaClientBridgeHelpersIOS.Clear();
                        onSuccess?.Invoke();
                    },
                    onError: error => onError?.Invoke(error)
                )
            );
        }
        
        #endregion
        
        #region Token
        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeGetAccessToken(XsollaClientBridgeHelpersIOS.XsollaUnityBridgeJsonCallbackDelegate callback, Int64 callbackData);
        public void GetAccessToken(GetAccessTokenResultFunc onSuccess, ErrorFunc onError)
        {
            _XsollaUnityBridgeGetAccessToken(
                callback: XsollaClientBridgeHelpersIOS.OnXsollaUnityBridgeJsonCallback, 
                callbackData: XsollaClientBridgeHelpersIOS.CreateCallbackData("GetAccessToken",
                    onSuccess: result => onSuccess?.Invoke(XsollaStoreClientHelpers.JsonToToken(result)),
                    onError: error => onError?.Invoke(error)
                )
            );
        }
        
        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeUpdateAccessToken(string token, XsollaClientBridgeHelpersIOS.XsollaUnityBridgeJsonCallbackDelegate callback, Int64 callbackData);
        public void UpdateAccessToken(string token, UpdateAccessTokenResultFunc onSuccess, ErrorFunc onError)
        {
            _XsollaUnityBridgeUpdateAccessToken(
                token: token,
                callback: XsollaClientBridgeHelpersIOS.OnXsollaUnityBridgeJsonCallback, 
                callbackData: XsollaClientBridgeHelpersIOS.CreateCallbackData("UpdateAccessToken",
                    onSuccess: result => onSuccess?.Invoke(),
                    onError: error => onError?.Invoke(error)
                )
            );
        }
        
        #endregion
        
        #region Storefront
        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeGetAppleStorefront(XsollaClientBridgeHelpersIOS.XsollaUnityBridgeJsonCallbackDelegate callback, Int64 callbackData);
        public void GetAppleStorefront(GetAppleStorefrontResultFunc onSuccess, ErrorFunc onError) => GetAppleStorefront_(onSuccess, onError);
        public static void GetAppleStorefront_(GetAppleStorefrontResultFunc onSuccess, ErrorFunc onError)
        {
            _XsollaUnityBridgeGetAppleStorefront(
                callback: XsollaClientBridgeHelpersIOS.OnXsollaUnityBridgeJsonCallback, 
                callbackData: XsollaClientBridgeHelpersIOS.CreateCallbackData("GetAppleStorefront",
                    onSuccess: result => onSuccess?.Invoke(XsollaStoreClientHelpers.JsonToStorefront(result)),
                    onError: error => onError?.Invoke(error)
                )
            );
        }
        
        #endregion
        
        #region Distribution
        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeGetAppleDistribution(XsollaClientBridgeHelpersIOS.XsollaUnityBridgeJsonCallbackDelegate callback, Int64 callbackData);
        public static void GetAppleDistribution_(GetAppleDistributionResultFunc onSuccess, ErrorFunc onError)
        {
            _XsollaUnityBridgeGetAppleDistribution(
                callback: XsollaClientBridgeHelpersIOS.OnXsollaUnityBridgeJsonCallback, 
                callbackData: XsollaClientBridgeHelpersIOS.CreateCallbackData("GetAppleDistribution",
                    onSuccess: result => onSuccess?.Invoke(XsollaStoreClientHelpers.JsonToDistribution(result)),
                    onError: error => onError?.Invoke(error)
                )
            );
        }
        
        #endregion

        #region Cancel Transaction

        [DllImport("__Internal")]
        private static extern void _XsollaUnityBridgeCancelTransaction();
        
        public static void CancelTransaction()
        {
            _XsollaUnityBridgeCancelTransaction();
        }

        #endregion
    }

}
#endif