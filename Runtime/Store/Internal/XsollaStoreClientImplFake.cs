using System;
using System.Collections.Generic;
using UnityEngine;
using Xsolla.SDK.Common;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Store
{
    internal class XsollaStoreClientImplFake : IXsollaStoreClient
    {
        private const string Tag = "XsollaStoreClientImplFake";

        private XsollaClientConfiguration _configuration = null;
        
        #region Interface implementation
        
        public void Initialize(XsollaClientConfiguration configuration, 
            InitializeResultFunc onSuccess, ErrorFunc onError,
            PurchaseProductResultFunc onSuccessPurchaseProduct, ErrorFunc onErrorPurchase
        ) {
            XsollaLogger.Debug(Tag, $"Initialize {configuration}");
            XsollaLogger.Debug(Tag, $"Initialize JSON: {XsollaClientHelpers.ConfigurationToJson(configuration)}");

            _configuration = configuration;
            
            onSuccess?.Invoke();
        }
        
        public void RestorePurchases(RestorePurchasesResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "Restore");

            var result = new List<XsollaStoreClientPurchasedProduct>();
            onSuccess?.Invoke(result.ToArray());
        }
        
        public void FetchProducts( string[] productIds, FetchProductsResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "ProductsRequest");
            
            onSuccess?.Invoke(
                productIds.Map(
                    mapper: item => 
                        XsollaStoreClientProduct.Builder.Create()
                        .SetSku(item)
                        .SetTitle(item)
                        .SetDescription(item)
                        .SetCurrency("USD")
                        .SetFormattedPrice("USD 0.00")
                        .SetPrice(0)
                        .SetIconUrl("")
                        .Build()
                )
            );
        }
        
        public void PurchaseProduct(string sku, string developerPayload, XsollaStoreClientPurchaseArgs args, PurchaseProductResultFunc onSuccess, ErrorFunc onError)
        {
	        var finalDeveloperPayload = args.developerPayload ?? developerPayload;

            XsollaLogger.Debug(Tag, $"Purchase {sku} - {finalDeveloperPayload} - {XsollaStoreClientHelpers.PurchaseToJson(sku, finalDeveloperPayload, args.externalId)}");

            if (_configuration != null && _configuration.simpleMode == XsollaClientConfiguration.SimpleMode.WebShop)
            {
                if (_configuration.settings.webShopUrl == "" || _configuration.userId == "")
                {
                    XsollaLogger.Debug(Tag, "WebShop URL or User Id is empty");
                    onError?.Invoke("WebShop URL or User Id is empty");
                    return;
                }
                
                var url = $"{_configuration.settings.webShopUrl}?user-id={_configuration.userId}&sku={sku}&redirect-url={_configuration.settings.redirectSettings.redirectUrl}";
                
                XsollaLogger.Debug(Tag, $"WebShop: {url}");
                
                Application.OpenURL(url);
                
                return;
            }
            
            onSuccess?.Invoke(
                XsollaStoreClientPurchasedProduct.Builder.Create()
                    .SetOrderId(0) 
                    .SetTransactionId(Guid.NewGuid().ToString())
                    .SetInvoiceId(Guid.NewGuid().ToString()) 
                    .SetSku(sku)
                    .SetQuantity(1) 
                    .SetStatus(XsollaStoreClientPurchasedProduct.Status.Paid)
                    .SetReceipt("")
                    .SetDeveloperPayload(finalDeveloperPayload)
                    .Build()
            );
        }
        
        public void ConsumeProduct(string sku, int quantity, string transactionId, ConsumeProductResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, $"Consume  {sku} - {XsollaStoreClientHelpers.ConsumeToJson(sku, quantity, transactionId, receipt: string.Empty)}");

            onSuccess?.Invoke();
        }
        
        public void ValidatePurchase(string receipt, ValidatePurchaseResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "ValidateReceipt");
            
            onSuccess?.Invoke(true);
        }

        public void Deinitialize(DeinitializeResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "Deinitialize");
            
            onSuccess?.Invoke();
        }

        public void GetAccessToken(GetAccessTokenResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "GetAccessToken");
            
            onSuccess?.Invoke("<fake-access-token>");
        }

        public void GetAppleStorefront(GetAppleStorefrontResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "GetAppleStorefront");
            
            onError?.Invoke("unsupported platform");
        }

        public void UpdateAccessToken(string token, UpdateAccessTokenResultFunc onSuccess, ErrorFunc onError)
        {
            XsollaLogger.Debug(Tag, "UpdateAccessToken");
            
            onSuccess?.Invoke();
        }

        #endregion
    }
}

