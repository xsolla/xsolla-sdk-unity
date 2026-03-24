#if UNITY_STANDALONE || UNITY_WEBGL || UNITY_EDITOR //&& DISABLED_TMP
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Xsolla.Auth;
using Xsolla.Catalog;
using Xsolla.Core;
using Xsolla.GetUpdates;
using Xsolla.Inventory;
using Xsolla.SDK.Common;
using Xsolla.Orders;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Store
{
    internal class XsollaStoreClientImplStandalone : IXsollaStoreClient
    {
        private const string Tag = "XsollaStoreClientImplSDK";

        [Serializable, UsedImplicitly]
        private class ReceiptForJson
        {
            public string tag;
            public string sku;
            public string uid;
        }

        private ISimpleFuture<bool, Error> _initializedFuture;
        private ISimpleFuture<bool, Error> _authTokenFuture;
        private ISimpleFuture<StoreItem[], Error> _productsRequestFuture;
        private XsollaClientConfiguration configuration;
        private XsollaSettings _settings;

        private PurchaseProductResultFunc onUnorderedPurchaseProduct;

        private const int PageLimit = 50;

        #region Interface implementation

        public XsollaStoreClientImplStandalone()
        {
            _initializedFuture = SimpleFuture.Create<bool, Error>(out var promise);
        }

        public void Initialize(XsollaClientConfiguration configuration,
            InitializeResultFunc onSuccess, ErrorFunc onError,
            PurchaseProductResultFunc onSuccessPurchaseProduct, ErrorFunc onErrorPurchase
        ) {
            XsollaLogger.Debug(Tag, $"Initialize: {XsollaClientHelpers.ConfigurationToJson(configuration)}");

            this.configuration = configuration;
            _settings = FillFromConfiguration(configuration);

            onUnorderedPurchaseProduct = onSuccessPurchaseProduct;
            
            _initializedFuture.Promise.Complete(true);
            
            Authenticate(
                onSuccess: _ => onSuccess?.Invoke(), 
                onError: error => onError?.Invoke(error.ToString())
            );
        }

        public void RestorePurchases(RestorePurchasesResultFunc onSuccess, ErrorFunc onError)
        {
            Authenticate(
                onSuccess: exists =>
                {
                    if (!exists)
                    {
                        onError?.Invoke(Error.UnknownError.ToString());
                        return;
                    }
                    
                    if (configuration.settings.webhooksMode == XsollaClientSettings.WebhooksMode.EventsApi)
                        UpdateWebhooks(
                            onSuccess: products =>
                            {
                                onSuccess?.Invoke(products);
                                
                                OrderTrackingService.SetUnorderedPurchaseEvent(
                                    _settings,
                                    onEvent: evts =>
                                    {
                                        foreach (var evt in evts)
                                        {
                                            onUnorderedPurchaseProduct?.Invoke(
                                                XsollaStoreClientPurchasedProduct.Builder.Create()
                                                    .SetOrderId(evt.order_id)
                                                    .SetTransactionId(evt.transaction_id ?? Guid.NewGuid().ToString())
                                                    .SetSku(evt.sku)
                                                    .SetQuantity(evt.quantity)
                                                    .SetStatus(evt.order_status)
                                                    .SetReceipt(XsollaClientHelpers.ToJson(evt))
                                                    .SetDeveloperPayload(evt.custom_parameters.TryGetValue("custom_payload", out var parameter) ? parameter : string.Empty)
                                                    .Build()
                                            );
                                        }
                                    },
                                    intervalSec: configuration.settings.localPurchasesRestoreInterval
                                );
                            }, 
                            onError: onError
                        );
                    else
                        RestorePurchases_(onSuccess, onError);
                },
                onError: error => onError?.Invoke(error.ToString())
            );
            
        }

        private void RestorePurchases_(RestorePurchasesResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "Restore");

            if (!configuration.settings.localPurchasesRestore)
            {
                XsollaLogger.Debug(Tag, "Restore: ignore local purchases restore = false");
                
                onSuccess?.Invoke(Array.Empty<XsollaStoreClientPurchasedProduct>());
                return;
            }
            
                   
            var result = new List<InventoryItem>();

            RestoreRecursive(
                onSuccess: () =>
                {
                    onSuccess?.Invoke(result.ToArray().Map(item => 
                        XsollaStoreClientPurchasedProduct.Builder.Create()
                            .SetOrderId(0) 
                            .SetTransactionId(Guid.NewGuid().ToString()) 
                            .SetSku(item.sku)
                            .SetQuantity(1) 
                            .SetStatus(XsollaStoreClientPurchasedProduct.Status.Restored)
                            .SetReceipt("")
                            .Build()
                    ));
                },
                onError: error => onError?.Invoke(error.ToString()),
                limit: PageLimit,
                offset: 0,
                result
            );
        }

        private void UpdateWebhooks(RestorePurchasesResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "UpdateWebhooks");
            
            XsollaGetUpdates.GetPaymentEvents(
                _settings,
                onSuccess: result =>
                {
                    onSuccess?.Invoke(result.Map(evt =>
                        XsollaStoreClientPurchasedProduct.Builder.Create()
                            .SetOrderId(evt.order_id)
                            .SetTransactionId(evt.transaction_id ?? Guid.NewGuid().ToString())
                            .SetSku(evt.sku)
                            .SetQuantity(evt.quantity)
                            .SetStatus(evt.order_status)
                            .SetReceipt(XsollaClientHelpers.ToJson(evt))
                            .SetDeveloperPayload(evt.custom_parameters.TryGetValue("custom_payload", out var parameter) ? parameter : string.Empty)
                            .Build()
                    ));
                    
                    
                },
                onError: error => onError?.Invoke(error.ToString()),
                markProcessed: true
            );
        }
        
        public void FetchProducts( string[] productIds, FetchProductsResultFunc onSuccess, ErrorFunc onError)
        {
            Authenticate(
                onSuccess: exists =>
                {
                    if (!exists)
                    {
                        onError?.Invoke(Error.UnknownError.ToString());
                        return;
                    }

                    FetchProducts_(productIds, onSuccess, onError);
                },
                onError: error => onError?.Invoke(error.ToString())
            );
        }
        
        private void FetchProducts_( string[] productIds, FetchProductsResultFunc onSuccess, ErrorFunc onError)
        {
            ProductsRequest_(productIds).OnComplete(
                onSuccess: items =>
                {
                    onSuccess?.Invoke(
                        items.MapWithFilter(
                            filter: item => productIds.Find(_ => _.Equals(item.sku)),
                            mapper: item =>
                                XsollaStoreClientProduct.Builder.Create()
                                    .SetSku(item.sku)
                                    .SetTitle(item.name)
                                    .SetDescription(item.description)
                                    .SetCurrency(item.price?.currency)
                                    .SetIconUrl(item.image_url)
                                    .SetPrice(item.price != null ? Convert.ToInt64(item.price.GetAmountWithoutDiscount() * 1e6) : 0)
                                    .SetFormattedPrice(item.price != null ? CurrencyFormatter.ToCurrency((decimal)item.price.GetAmountWithoutDiscount(), item.price.currency, item.locale) : (item.is_free ? "Free" : "0.00"))
                                    .SetPriceWithDiscount(item.price != null ? Convert.ToInt64(item.price.GetAmount() * 1e6) : 0)
                                    .SetFormattedPriceWithDiscount(item.price != null ? CurrencyFormatter.ToCurrency((decimal)item.price.GetAmount(), item.price.currency, item.locale) : (item.is_free ? "Free" : "0.00"))
                                    .SetPriceWithoutDiscount(item.price != null ? Convert.ToInt64(item.price.GetAmountWithoutDiscount() * 1e6) : 0)
                                    .SetFormattedPriceWithoutDiscount(item.price != null ? CurrencyFormatter.ToCurrency((decimal)item.price.GetAmountWithoutDiscount(), item.price.currency, item.locale) : (item.is_free ? "Free" : "0.00"))
                                    .SetDiscountPercentage(item.promotions != null && item.promotions.Length > 0 && item.promotions[0].discount != null ? item.promotions[0].discount.percent : string.Empty)
                                    .Build()
                        )
                    );
                }, 
                onError: error => onError?.Invoke(error.ToString())
            );
        }
        
        public void PurchaseProduct(string sku, string developerPayload, XsollaStoreClientPurchaseArgs args, PurchaseProductResultFunc onSuccess, ErrorFunc onError)
        {
            Authenticate(
                onSuccess: exists =>
                {
                    if (!exists)
                    {
                        onError?.Invoke(Error.UnknownError.ToString());
                        return;
                    }

                    PurchaseProduct_(sku, developerPayload, args, onSuccess, onError);
                },
                onError: error => onError?.Invoke(error.ToString())
            );
        }

        private void PurchaseProduct_(string sku, string developerPayload, XsollaStoreClientPurchaseArgs args, PurchaseProductResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "Purchase");
            
            PurchaseParams purchaseParams = null;

            var locale = configuration.GetCurrentLocale();

            // Xsolla API mixes up the definitions... yet again, this is a language code (e.g. `US`, `DE`, etc.).
            getPurchaseParams().locale = locale?.language;
            getPurchaseParams().country = locale?.country;

            getPurchaseParams().currency = locale?.currencyCode;

            if (!string.IsNullOrEmpty(configuration.userId))
                AddCustomParam("custom_user_id", configuration.userId);

            if (!string.IsNullOrEmpty(developerPayload))
                AddCustomParam("custom_payload", developerPayload);

            if (!string.IsNullOrEmpty(args?.externalId))
                getPurchaseParams().external_id = args.externalId;

            if (args?.paymentMethodId != null && args.paymentMethodId >= 0)
                getPurchaseParams().payment_method = args.paymentMethodId;

            if (!string.IsNullOrEmpty(configuration.trackingId))
                getPurchaseParams().tracking_id = configuration.trackingId;

            if (configuration.settings.useBuyButtonSolution)
                getPurchaseParams().use_buy_button_solution = configuration.settings.useBuyButtonSolution;

#pragma warning disable CS0618 // Type or member is obsolete
            if (args?.allowTokenOnlyFinishedStatusWithoutOrderId != null && args.allowTokenOnlyFinishedStatusWithoutOrderId)
                getPurchaseParams().allow_token_only_finished_status_without_orderId = args.allowTokenOnlyFinishedStatusWithoutOrderId;
#pragma warning restore CS0618 // Type or member is obsolete

            if (configuration.simpleMode == XsollaClientConfiguration.SimpleMode.ServerTokens || !string.IsNullOrEmpty(args?.paymentToken))
            {
                if (!string.IsNullOrEmpty(args?.paymentToken))
                {
                    XsollaLogger.Debug(Tag, $"PurchaseWithToken: {args.paymentToken}");

                    XsollaCatalog.PurchaseWithToken(
                        _settings,
                        paymentToken: args.paymentToken,
                        onSuccess: OnSuccess,
                        onError: error => onError?.Invoke(error.ToString()),
                        purchaseParams: purchaseParams
                    );
                }
                else
                {
                    onError?.Invoke("[PurchaseProduct] No payment token provided for `ServerTokens`");
                }
            }
            else if (configuration.simpleMode == XsollaClientConfiguration.SimpleMode.WebShop)
            {
                XsollaCatalog.PurchaseWithWebshop(
                    _settings,
                    itemSku: sku,
                    webshopUrl: configuration.settings.webShopUrl,
                    userId: configuration.userId,
                    redirectUrl: configuration.settings.redirectSettings.redirectUrl,
                    onSuccess: OnSuccess,
                    onError: error => onError?.Invoke(error.ToString()),
                    purchaseParams: purchaseParams
                );
            }
            else
            {
                XsollaCatalog.Purchase(
                    _settings,
                    itemSku: sku,
                    onSuccess: OnSuccess,
                    onError: error => onError?.Invoke(error.ToJson()),
                    onBrowseClosed: null,
                    purchaseParams: purchaseParams
                );
            }

            void AddCustomParam(string key, string value)
            {
                getPurchaseParams().custom_parameters ??= new Dictionary<string, object>();
                getPurchaseParams().custom_parameters.Add(key, value);
            }

            void OnSuccess(OrderStatus orderStatus)
            {
                var transactionId = orderStatus.transaction_id ?? Guid.NewGuid().ToString();

                var receipt = orderStatus.receipt;
                if (string.IsNullOrEmpty(receipt) && orderStatus.content != null) {
                    // To have identical receipts with Android.
                    var jsonString = XsollaClientHelpers.ToJson(new ReceiptForJson
                    {
                        // A pre-generated UUID for tagging only to be able to differentiate
                        // between receipts of different billing implementations.
                        tag = "ed01a8e0-7bb4-4bec-95c8-a6f2e1bfb961",
                        sku = orderStatus.content.items[0].sku,
                        uid = orderStatus.order_id.ToString()
                    });

                    var jsonBytes = Encoding.UTF8.GetBytes(jsonString);

                    receipt = Convert.ToBase64String(jsonBytes)
                        .TrimEnd('=')
                        .Replace('+', '-')
                        .Replace('/', '_');
                }

                onSuccess?.Invoke(
                    XsollaStoreClientPurchasedProduct.Builder.Create()
                        .SetOrderId(orderStatus.order_id)
                        .SetInvoiceId(transactionId)
                        .SetTransactionId(transactionId)
                        .SetSku(orderStatus.content != null ? orderStatus.content.items[0].sku : sku)
                        .SetQuantity(orderStatus.content != null ? orderStatus.content.items[0].quantity : 1)
                        .SetStatus(XsollaStoreClientPurchasedProduct.Status.Paid)
                        .SetReceipt(receipt)
                        .SetDeveloperPayload(developerPayload)
                        .Build()
                );
            }

            PurchaseParams getPurchaseParams()
            {
                return purchaseParams ??= new PurchaseParams();
            }
        }
        
        public void ConsumeProduct(string sku, int quantity, string transactionId, ConsumeProductResultFunc onSuccess, ErrorFunc onError)
        {
            Authenticate(
                onSuccess: exists =>
                {
                    if (!exists)
                    {
                        onError?.Invoke(Error.UnknownError.ToString());
                        return;
                    }

                    ConsumeProduct_(sku, quantity, transactionId, onSuccess, onError);
                },
                onError: error => onError?.Invoke(error.ToString())
            );
        }
        
        private void ConsumeProduct_(string sku, int quantity, string transactionId, ConsumeProductResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "Consume");

            if (!configuration.settings.localPurchasesRestore)
            {
                XsollaLogger.Debug(Tag, "Consume: ignore local purchases restore = false");
                
                onSuccess?.Invoke();
                return;
            }
            
            if (configuration.settings.webhooksMode == XsollaClientSettings.WebhooksMode.EventsApi)
            {
                if (XsollaGetUpdates.transactionIdCache.TryGetValue(transactionId, out var cachedEvent) && cachedEvent.sku == sku)
                {
                    XsollaGetUpdates.EventProcessed(_settings, 
                        cachedEvent.id,
                        onSuccess: () => onSuccess?.Invoke(),
                        onError: error => onError?.Invoke(error.ToString())
                    );

                    if (cachedEvent.subid != -1)
                    {
                        XsollaGetUpdates.EventProcessed(_settings,
                            cachedEvent.subid,
                            onSuccess: () => onSuccess?.Invoke(),
                            onError: error => onError?.Invoke(error.ToString())
                        );
                    }
                }
                else
                {
                    onError?.Invoke("Transaction ID not found in cache or SKU mismatch.");
                }
            }
            else
            {
                var item = new ConsumeItem
                {
                    sku = sku,
                    quantity = quantity,
                    instance_id = null
                };

                XsollaInventory.ConsumeInventoryItem(
                    _settings,
                    item: item,
                    onSuccess: () => onSuccess?.Invoke(),
                    onError: error => onError?.Invoke(error.ToString())
                );
            }
        }
        
        public void ValidatePurchase(string receipt, ValidatePurchaseResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "Validate");
            
            if (XsollaStoreClientHelpers.TryJsonToReceipt(receipt, out var receiptData))
            {
                if (receiptData.orderStatus == XsollaStoreClientPurchasedProduct.Status.Restored)
                {
                    onSuccess?.Invoke(true);
                    return;
                }

                if (receiptData.orderId <= 0)
                {
                    onError?.Invoke("Invalid receipt: missing order ID");
                    return;
                }
                
                XsollaOrders.CheckOrderStatus(
                    _settings,
                    orderId: receiptData.orderId,
                    onSuccess: status =>
                    {
                        onSuccess?.Invoke(status.status == "done");
                    },
                    onError: error => onError?.Invoke(error.ToString())
                );
            }
            else
            {
                onError?.Invoke(Error.UnknownError.ToString());
            }
        }

        public void Deinitialize(DeinitializeResultFunc onSuccess, ErrorFunc onError)
        {
            _authTokenFuture = null;
            _productsRequestFuture = null;
            
            onSuccess?.Invoke();
        }

        public void GetAccessToken(GetAccessTokenResultFunc onSuccess, ErrorFunc onError)
        {
            Authenticate(
                onSuccess: res => onSuccess?.Invoke(_settings.XsollaToken.AccessToken),
                onError: error => onError?.Invoke(error.ToString())
            );
        }
        
        public void GetAppleStorefront(GetAppleStorefrontResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "GetAppleStorefront");
            
            onError?.Invoke("unsupported platform");
        }

        public void UpdateAccessToken(string token, UpdateAccessTokenResultFunc onSuccess, ErrorFunc onError)
        {
            if (_authTokenFuture?.isCompleted == true)
            {
                SetToken();
            }
            else if (_authTokenFuture != null)
            {
                _authTokenFuture.OnComplete(
                    onSuccess: res => SetToken(),
                    onError: error => SetToken()
                );
            }
            else
                onError?.Invoke(Error.UnknownError.ToString());
            
            void SetToken()
            {
                _settings.XsollaToken.Create(token, isBasedOnDeviceId: false);
                onSuccess?.Invoke();
            }
        }
        
        #endregion

        #region Private helpers

        private void Authenticate(Action<bool> onSuccess = null, Action<Error> onError = null)
        {
            if (!_initializedFuture.isCompleted)
                XsollaLogger.Error(Tag,"Called without initialization");
            
            _initializedFuture.OnComplete(
                onSuccess: _ => Authenticate_().OnComplete(
                    onSuccess: onSuccess,
                    onError: onError
                ),
                onError: error => onError?.Invoke(error)
            );
        }
        
        private ISimpleFuture<bool, Error> Authenticate_()
        {
            if (_authTokenFuture != null)
                return _authTokenFuture;
            
            XsollaLogger.Debug(Tag, $"Authenticate");
            
            var future = SimpleFuture.Create<bool, Error>(out var promise);

            _authTokenFuture = future;

            XsollaAuth.AuthViaXsollaLauncher(
                _settings,
                onSuccess: () =>
                {
                    XsollaLogger.Debug(Tag, "Authenticate Via Launcher onSuccess");
                    promise.Complete(true);
                },
                onError: error =>
                {
                    XsollaLogger.Debug(Tag, $"Authenticate Via Launcher onError {error}, trying to auth by saved token");
                    
                    XsollaAuth.AuthBySavedToken(
                        _settings,
                        onSuccess: () =>
                        {
                            XsollaLogger.Debug(Tag, "Authenticate By Saved Token onSuccess");
                            promise.Complete(true);
                        },
                        onError: error =>
                        {
                            if (configuration.settings.socialProvider.HasValue)
                            {
                                var settingsSocialProvider = configuration.settings.socialProvider.Value;

                                XsollaLogger.Debug(Tag, "Trying to authenticate using the social access token:\n" +
                                    $"  Provider={settingsSocialProvider.name}\n" +
                                    $"  AccessToken={settingsSocialProvider.accessToken}"
                                );

                                XsollaAuth.AuthWithSocialNetworkAccessToken(
                                    _settings,
                                    accessToken: settingsSocialProvider.accessToken,
                                    accessTokenSecret: null,
                                    openId: null,
                                    provider:  settingsSocialProvider.name,
                                    onSuccess: () =>
                                    {
                                        XsollaLogger.Debug(Tag, $"Successfully authenticated using the social access token (" +
                                            $"provider={settingsSocialProvider.name} " +
                                            $"accessToken={settingsSocialProvider.accessToken}" +
                                            $"):\n{_settings.XsollaToken.AccessToken}"
                                        );

                                        promise.Complete(true);
                                    },
                                    onError: error =>
                                    {
                                        XsollaLogger.Error(Tag, "Failed to authenticate using the social access token  (" +
                                            $"provider={settingsSocialProvider.name} " +
                                            $"accessToken={settingsSocialProvider.accessToken}" +
                                            $"):\n{error}"
                                        );

                                        AuthenticateUsingFallback(error.ToString());
                                    }
                                );
                            }
                            else
                            {
                                AuthenticateUsingFallback(
                                    $"Authenticate By Saved Token onError {error}"
                                );
                            }

                            void AuthenticateUsingFallback(string errorMsg)
                            {
                                if (DataProvider.GetPlatform() == RuntimePlatform.WebGLPlayer)
                                {
                                    XsollaLogger.Debug(Tag, errorMsg);
                                    promise.CompleteWithError(new Error(errorMessage: errorMsg));
                                    return;
                                }
                                
                                XsollaLogger.Debug(Tag, $"{errorMsg}\nTrying to auth by device ID..");

                                XsollaAuth.AuthViaDeviceID(
                                    _settings,
                                    onSuccess: () =>
                                    {
                                        XsollaLogger.Debug(Tag, "Authenticate onSuccess");
                                        promise.Complete(true);
                                    },
                                    onError: error =>
                                    {
                                        XsollaLogger.Debug(Tag, $"Authenticate onError {error}");
                                        promise.CompleteWithError(error);
                                    }
                                );
                            }
                        }
                    );  
                }
            );

            return future;
        }
        
        private void RestoreRecursive(Action onSuccess, Action<Error> onError, int limit, int offset, List<InventoryItem> result)
        {
            XsollaInventory.GetInventoryItems(
                _settings,
                onSuccess: items =>
                {
                    result.AddRange(items.items);
                    
                    if (items.has_more)
                        RestoreRecursive(onSuccess, onError, limit, offset + limit, result);
                    else
                        onSuccess?.Invoke();
                },
                onError: onError,
                limit: limit,
                offset: offset
            );
        }

        private ISimpleFuture<StoreItem[], Error> ProductsRequest_(string[] productIds)
        {
            if (_productsRequestFuture != null)
                return _productsRequestFuture;
            
            XsollaLogger.Debug(Tag, $"ProductsRequest: products={XsollaClientHelpers.ToJson(productIds)}");
            
            var future = SimpleFuture.Create<StoreItem[], Error>(out var promise);

            _productsRequestFuture = future;

            var locale = configuration.GetCurrentLocale();
            var language = string.IsNullOrEmpty(locale?.language) ? null : locale.language;
            var country = string.IsNullOrEmpty(locale?.country) ? null : locale.country;

            XsollaLogger.Debug(Tag, $"ProductsRequest: language={language} country={country} (default={LocaleInfo.Default.language}-{LocaleInfo.Default.country} fallback={configuration.fallbackToDefaultLocaleIfNotSet})");

            XsollaCatalog.GetItems(
                _settings,
                onSuccess: items =>
                {
                    var finalLocale = items.geoLocale ?? locale?.cultureInfo;

                    XsollaLogger.Debug(Tag, $"ProductsRequest_: finalLocale={finalLocale}");

                    if (finalLocale != null)
                    {
                        foreach (var item in items.items) {
                            item.locale = finalLocale;
                        }
                    }

                    promise.Complete(items.items);
                },
                onError: error =>
                {
                    promise.CompleteWithError(error);
                },
                limit: PageLimit,
                // This is actually a language (e.g. 'EN', 'DE', etc.), not a fully-fledged locale (e.g. 'en_US', 'de_DE').
                locale: language,
                country: country,
                requestGeoLocale: configuration.fetchProductsWithGeoLocale
            );

            return future;
        }
        
        #endregion

        private static XsollaSettings FillFromConfiguration(XsollaClientConfiguration configuration)
        {
            Info.SDK_NAME = Common.Constants.SDK_NAME;
            Info.SDK_VERSION = Common.Constants.SDK_VERSION;

            var settings = new XsollaSettings();

            settings.LogLevel = configuration.logLevel switch
            {
                XsollaLogLevel.Debug => LogLevel.InfoWarningsErrors,
                XsollaLogLevel.Warning => LogLevel.WarningsErrors,
                XsollaLogLevel.Error => LogLevel.Errors,
                _ => LogLevel.Errors
            };
            
            XDebug.SetLogLevel(settings.LogLevel);
            
            var callback = XsollaLogger.GetOnLogCallback();
            if (callback != null) {
                XDebug.SetOnLogCallback( (lvl, msg) =>
                {
                    callback?.Invoke(
                        lvl switch
                        {
                            LogLevel.InfoWarningsErrors => XsollaLogLevel.Debug,
                            LogLevel.WarningsErrors => XsollaLogLevel.Warning,
                            LogLevel.Errors => XsollaLogLevel.Error,
                            _ => XsollaLogLevel.None
                        },
                        msg
                    );
                });
            }
            
            settings.StoreProjectId = configuration.settings.projectId.ToString();
            settings.IsSandbox = configuration.sandbox;
            settings.LoginId = configuration.settings.loginId;
            settings.OAuthClientId = configuration.settings.oauthClientId;

            settings.InAppBrowserEnabled = true;
            settings.ExternalBrowserEnabled = configuration.settings.webViewType == XsollaClientSettings.WebViewType.External;
            settings.EventApiEnabled = configuration.settings.webhooksMode == XsollaClientSettings.WebhooksMode.EventsApi;
                
            settings.CallbackUrl = configuration.settings.redirectSettings.redirectUrl;
            
            settings.DesktopRedirectPolicySettings.ReturnUrl = configuration.settings.redirectSettings.redirectUrl;
            settings.DesktopRedirectPolicySettings.RedirectButtonCaption = configuration.settings.redirectSettings.redirectButtonText;
            settings.DesktopRedirectPolicySettings.Delay = configuration.settings.redirectSettings.redirectDelay;

            if (configuration.settings.uiSettings.themeStyle == XsollaClientSettings.ThemeStyle.Custom)
            {
                settings.DesktopPayStationUISettings.paystationThemeId = configuration.settings.uiSettings.customTheme;
                settings.WebglPayStationUISettings.paystationThemeId = configuration.settings.uiSettings.customTheme;
            }
            else if (configuration.settings.uiSettings.themeStyle == XsollaClientSettings.ThemeStyle.Dark)
            {
                settings.DesktopPayStationUISettings.paystationThemeId = "63295aab2e47fab76f7708e3";
                settings.WebglPayStationUISettings.paystationThemeId = "63295aab2e47fab76f7708e3";
            }
            else if (configuration.settings.uiSettings.themeStyle == XsollaClientSettings.ThemeStyle.Light)
            {
                settings.DesktopPayStationUISettings.paystationThemeId = "63295a9a2e47fab76f7708e1";
                settings.WebglPayStationUISettings.paystationThemeId = "63295a9a2e47fab76f7708e1";
            }

            if (configuration.settings.uiSettings.themeSize != XsollaClientSettings.ThemeSize.Auto)
            {
                settings.DesktopPayStationUISettings.size = configuration.settings.uiSettings.themeSize.ToString().ToLowerInvariant();
                settings.WebglPayStationUISettings.size = configuration.settings.uiSettings.themeSize.ToString().ToLowerInvariant();
            }

            if (configuration.settings.closeButton != XsollaClientSettings.CloseButton.Auto)
            {
                settings.DesktopPayStationUISettings.showCloseButton =
                    configuration.settings.closeButton == XsollaClientSettings.CloseButton.Show;
                settings.WebglPayStationUISettings.showCloseButton =
                    configuration.settings.closeButton == XsollaClientSettings.CloseButton.Show;
            }
            
            if (!string.IsNullOrEmpty(configuration.accessToken))
                settings.XsollaToken.Create(configuration.accessToken, isBasedOnDeviceId: false);

            if (configuration.settings.uiSettings.visibleLogo != XsollaClientSettings.VisibleLogo.Auto)
            {
                settings.DesktopPayStationUISettings.visibleLogo =
                    configuration.settings.uiSettings.visibleLogo == XsollaClientSettings.VisibleLogo.Show;
                settings.WebglPayStationUISettings.visibleLogo =
                    configuration.settings.uiSettings.visibleLogo == XsollaClientSettings.VisibleLogo.Show;
            }

            settings.CustomPayStationDomainProduction = configuration.customPayStationDomainProduction;
            settings.CustomPayStationDomainSandbox = configuration.customPayStationDomainSandbox;

            return settings;
        }
    }
}

#endif
