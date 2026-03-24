using JetBrains.Annotations;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Xsolla.SDK.Common;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// Request for store client products.
    /// </summary>
    [Serializable]
    internal class XsollaStoreClientProductsRequest
    {
        /// <summary>
        /// Array of product SKUs.
        /// </summary>
        public readonly string[] products;

        /// <summary>
        /// Initializes a new instance of the <see cref="XsollaStoreClientProductsRequest"/> class.
        /// </summary>
        /// <param name="products">Array of product SKUs.</param>
        public XsollaStoreClientProductsRequest(string[] products)
        {
            this.products = products;
        }
    }

    /// <summary>
    /// Result containing store client products.
    /// </summary>
    [Serializable]
    internal class XsollaStoreClientProductsResult
    {
        /// <summary>
        /// Array of store client products.
        /// </summary>
        public XsollaStoreClientProduct[] products;

        /// <summary>
        /// Initializes a new instance with empty products.
        /// </summary>
        public XsollaStoreClientProductsResult()
        {
            products = Array.Empty<XsollaStoreClientProduct>();
        }

        /// <summary>
        /// Initializes a new instance with specified products.
        /// </summary>
        /// <param name="products">Array of products.</param>
        public XsollaStoreClientProductsResult(XsollaStoreClientProduct[] products)
        {
            this.products = products;
        }
    }

    /// <summary>
    /// Represents a store client product.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientProduct
    {
        /// <summary>Product SKU.</summary>
        public string sku = "";
        /// <summary>Product title.</summary>
        public string title = "";
        /// <summary>Product description.</summary>
        public string description = "";
        /// <summary>Product icon URL.</summary>
        public string iconUrl = "";

        /// <summary>Currency ISO code.</summary>
        public string currency = "";

        /// <summary>Base price in micros (before discount). Same as <see cref="priceWithoutDiscount"/>.</summary>
        public long price = 0;

        /// <summary>Formatted base price string (before discount).</summary>
        public string formattedPrice = "";

        /// <summary>Final price in micros after all discounts applied.</summary>
        public long priceWithDiscount = 0;

        /// <summary>Formatted final price string after discounts.</summary>
        public string formattedPriceWithDiscount = "";

        /// <summary>Base price in micros (before discount). Same as <see cref="price"/>.</summary>
        public long priceWithoutDiscount = 0;

        /// <summary>Formatted base price string (before discount).</summary>
        public string formattedPriceWithoutDiscount = "";

       /// <summary>Discount percentage string.</summary>
        public string discountPercentage = "";

        /// <summary>Product ID (SKU).</summary>
        public string productId => sku;
        /// <summary>Localized product title.</summary>
        public string localizedTitle => title;
        /// <summary>Localized product description.</summary>
        public string localizedDescription => description;
        /// <summary>Localized price string.</summary>
        public string localizedPriceString => formattedPrice;
        /// <summary>Currency code.</summary>
        public string currencyCode => currency;
        /// <summary>Localized price value.</summary>
        public long localizedPrice => price;
        /// <summary>Product icon URL.</summary>
        public string url => iconUrl;

        /// <summary>
        /// Builder for <see cref="XsollaStoreClientProduct"/>.
        /// </summary>
        public class Builder
        {
            private readonly XsollaStoreClientProduct _product = new XsollaStoreClientProduct();

            /// <summary>
            /// Creates a new builder instance.
            /// </summary>
            public static Builder Create() => new Builder();

            /// <summary>
            /// Sets the SKU.
            /// </summary>
            /// <param name="sku">Product SKU.</param>
            public Builder SetSku(string sku) { _product.sku = sku; return this; }

            /// <summary>
            /// Sets the title.
            /// </summary>
            /// <param name="title">Product title.</param>
            public Builder SetTitle(string title) { _product.title = title; return this; }

            /// <summary>
            /// Sets the description.
            /// </summary>
            /// <param name="description">Product description.</param>
            public Builder SetDescription(string description) { _product.description = description; return this; }

            /// <summary>
            /// Sets the currency code.
            /// </summary>
            /// <param name="currency">Currency ISO code.</param>
            public Builder SetCurrency(string currency) { _product.currency = currency; return this; }

            /// <summary>
            /// Sets the formatted price.
            /// </summary>
            /// <param name="formattedPrice">Formatted price string.</param>
            public Builder SetFormattedPrice(string formattedPrice) { _product.formattedPrice = formattedPrice; return this; }

            /// <summary>
            /// Sets the price in micros.
            /// </summary>
            /// <param name="price">Price in micros.</param>
            public Builder SetPrice(long price) { _product.price = price; return this; }

            /// <summary>
            /// Sets the icon URL.
            /// </summary>
            /// <param name="iconUrl">Product icon URL.</param>
            public Builder SetIconUrl(string iconUrl) { _product.iconUrl = iconUrl; return this; }

            /// <summary>
            /// Sets the discounted price in micros.
            /// </summary>
            /// <param name="priceWithDiscount">Discounted price in micros.</param>
            public Builder SetPriceWithDiscount(long priceWithDiscount) { _product.priceWithDiscount = priceWithDiscount; return this; }

            /// <summary>
            /// Sets the formatted discounted price.
            /// </summary>
            /// <param name="formattedPriceWithDiscount">Formatted discounted price string.</param>
            public Builder SetFormattedPriceWithDiscount(string formattedPriceWithDiscount) { _product.formattedPriceWithDiscount = formattedPriceWithDiscount; return this; }
            
            /// <summary>
            /// Sets the price without discount in micros.
            /// </summary>
            /// <param name="priceWithoutDiscount">Price without discount in micros.</param>
            public Builder SetPriceWithoutDiscount(long priceWithoutDiscount) { _product.priceWithoutDiscount = priceWithoutDiscount; return this; }
            
            /// <summary>
            /// Sets the formatted price without discount.
            /// </summary>
            /// <param name="formattedPriceWithoutDiscount">Formatted price without discount string.</param>
            public Builder SetFormattedPriceWithoutDiscount(string formattedPriceWithoutDiscount) { _product.formattedPriceWithoutDiscount = formattedPriceWithoutDiscount; return this; }

            /// <summary>
            /// Sets the discount percentage for the product.
            /// </summary>
            /// <param name="discountPercentage">Discount percentage value.</param>
            public Builder SetDiscountPercentage(string discountPercentage) { _product.discountPercentage = discountPercentage; return this; }
            
            /// <summary>
            /// Builds the product instance.
            /// </summary>
            public XsollaStoreClientProduct Build() => _product;
        }
    }

    /// <summary>
    /// Result containing purchased product data.
    /// </summary>
    [Serializable]
    internal class XsollaStoreClientPurchasedProductResult
    {
        /// <summary>
        /// Array of purchased product data.
        /// </summary>
        public XsollaStoreClientPurchasedProductData[] purchases;
    }

    /// <summary>
    /// Data for a purchased product.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientPurchasedProductData
    {
        /// <summary>Product SKU.</summary>
        public string sku;
        /// <summary>Transaction ID.</summary>
        public string transactionId;
        /// <summary>Order ID.</summary>
        public string orderId;
        /// <summary>Invoice ID.</summary>
        public string invoiceId;
        /// <summary>Status string.</summary>
        public string status;
        /// <summary>Receipt string.</summary>
        public string receipt;
    }

    /// <summary>
    /// Represents a purchased product.
    /// </summary>
    public class XsollaStoreClientPurchasedProduct
    {
        /// <summary>
        /// Status of the purchased product.
        /// </summary>
        public enum Status
        {
            /// <summary>Unknown status.</summary>
            Unknown = 0,
            /// <summary>New purchase.</summary>
            New = 1,
            /// <summary>Paid purchase.</summary>
            Paid = 2,
            /// <summary>Completed purchase.</summary>
            Done = 3,
            /// <summary>Canceled purchase.</summary>
            Canceled = 4,
            /// <summary>Restored purchase.</summary>
            Restored = 5,
            /// <summary>Restored by event.</summary>
            RestoredByEvent = 6,
            Free = 7,
        }

        /// <summary>Product SKU.</summary>
        public string sku;
        /// <summary>Order ID.</summary>
        public int orderId;
        /// <summary>Invoice ID.</summary>
        public string invoiceId;
        /// <summary>Transaction ID.</summary>
        public string transactionId;
        /// <summary>Receipt string.</summary>
        public string receipt;
        /// <summary>Quantity purchased.</summary>
        public int quantity;
        /// <summary>Status of the purchase.</summary>
        public Status status;
        /// <summary>Developer payload.</summary>
        public string developerPayload;

        /// <summary>
        /// Builder for <see cref="XsollaStoreClientPurchasedProduct"/>.
        /// </summary>
        public class Builder
        {
            private readonly XsollaStoreClientPurchasedProduct _product = new XsollaStoreClientPurchasedProduct();

            /// <summary>
            /// Creates a new builder instance.
            /// </summary>
            public static Builder Create() => new Builder();

            /// <summary>
            /// Sets the SKU.
            /// </summary>
            /// <param name="sku">Product SKU.</param>
            public Builder SetSku(string sku) { _product.sku = sku; return this; }

            /// <summary>
            /// Sets the order ID.
            /// </summary>
            /// <param name="orderId">Order ID.</param>
            public Builder SetOrderId(int orderId) { _product.orderId = orderId; return this; }

            /// <summary>
            /// Sets the invoice ID.
            /// </summary>
            /// <param name="invoiceId">Invoice ID.</param>
            public Builder SetInvoiceId(string invoiceId) { _product.invoiceId = invoiceId; return this; }

            /// <summary>
            /// Sets the quantity.
            /// </summary>
            /// <param name="quantity">Quantity purchased.</param>
            public Builder SetQuantity(int quantity) { _product.quantity = quantity; return this; }

            /// <summary>
            /// Sets the status.
            /// </summary>
            /// <param name="status">Purchase status.</param>
            public Builder SetStatus(Status status) { _product.status = status; return this; }

            /// <summary>
            /// Sets the status from a string.
            /// </summary>
            /// <param name="status">Status string.</param>
            public Builder SetStatus(string status)
            {
                switch(status)
                {
                    case "new": _product.status = Status.New; break;
                    case "purchased": _product.status = Status.Paid; break;
                    case "done": _product.status = Status.Done; break;
                    case "canceled": _product.status = Status.Canceled; break;
                    case "restored": _product.status = Status.Restored; break;
                    case "free": _product.status = Status.Free; break;
                    default: _product.status = Status.Unknown; break;
                };
                return this;
            }

            /// <summary>
            /// Sets the receipt.
            /// </summary>
            /// <param name="receipt">Receipt string.</param>
            public Builder SetReceipt(string receipt) { _product.receipt = receipt; return this; }

            /// <summary>
            /// Sets the transaction ID.
            /// </summary>
            /// <param name="transactionId">Transaction ID.</param>
            public Builder SetTransactionId(string transactionId) { _product.transactionId = transactionId; return this; }

            /// <summary>
            /// Sets the developer payload.
            /// </summary>
            /// <param name="developerPayload">Developer payload string.</param>
            public Builder SetDeveloperPayload(string developerPayload) { _product.developerPayload = developerPayload; return this; }

            /// <summary>
            /// Populates builder from purchased product data.
            /// </summary>
            /// <param name="data">Purchased product data.</param>
            public Builder FromData(XsollaStoreClientPurchasedProductData data)
            {
                _product.sku = data.sku;
                _product.orderId = string.IsNullOrEmpty(data.orderId) ? 0 : int.Parse(data.orderId);
                _product.invoiceId = data.invoiceId;
                _product.transactionId = string.IsNullOrEmpty(data.transactionId) ? Guid.NewGuid().ToString() : data.transactionId;
                _product.receipt = string.IsNullOrEmpty(data.receipt) ? "" : data.receipt;
                _product.quantity = 1;
                SetStatus(data.status);
                return this;
            }

            /// <summary>
            /// Builds the purchased product instance.
            /// </summary>
            public XsollaStoreClientPurchasedProduct Build() => _product;
        }

        /// <summary>
        /// Converts to a purchase receipt.
        /// </summary>
        /// <returns>Purchase receipt object.</returns>
        public XsollaStoreClientPurchaseReceipt ToReceipt()
        {
            return XsollaStoreClientPurchaseReceipt.Builder.Create()
                .SetOrderId(orderId)
                .SetProductId(sku)
                .SetTransactionId(transactionId)
                .SetInvoiceId(invoiceId)
                .SetReceipt(receipt)
                .SetOrderStatus(status)
                .SetDeveloperPayload(developerPayload)
                .Build();
        }
    }

    /// <summary>
    /// Store error message.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientError
    {
        /// <summary>
        /// Purchase error code.
        /// </summary>
        public XsollaStoreClientPurchaseErrorCode code = XsollaStoreClientPurchaseErrorCode.Unknown;

        /// <summary>
        /// Error message description.
        /// </summary>
        public string message;

        /// <summary>
        /// Initializes a new instance with error message.
        /// </summary>
        /// <param name="message">Error description.</param>
        public XsollaStoreClientError(string message)
        {
            this.message = message;
        }

        /// <summary>
        /// Initializes a new instance with error code and message.
        /// </summary>
        /// <param name="message">Error description.</param>
        /// <param name="code">Purchase error code.</param>
        public XsollaStoreClientError(string message, XsollaStoreClientPurchaseErrorCode code)
        {
            this.message = message;
            this.code = code;
        }

        /// <summary>
        /// Creates an error with message.
        /// </summary>
        /// <param name="message">Error description.</param>
        public static XsollaStoreClientError Message(string message) => new XsollaStoreClientError(message, XsollaStoreClientPurchaseErrorCode.Unknown);

        /// <summary>
        /// Creates an error with code and message.
        /// </summary>
        public static XsollaStoreClientError WithCode(string message, XsollaStoreClientPurchaseErrorCode code) => new XsollaStoreClientError(message, code);

        public override string ToString()
        {
            return $"({code}) - {message}";
        }
    }

    /// <summary>
    /// Error codes for purchase failures.
    /// </summary>
    public enum XsollaStoreClientPurchaseErrorCode
    {
        /// <summary>No error.</summary>
        None = 0,
        /// <summary>Internal error occurred.</summary>
        Internal = 1,
        /// <summary>Unknown error.</summary>
        Unknown = 2,
        /// <summary>Unauthorized or not allowed.</summary>
        Unauthorized = 3,
        /// <summary>Operation canceled by the user.</summary>
        Cancelled = 4,
        /// <summary>Operation aborted.</summary>
        Aborted = 5,
    }

    /// <summary>
    /// Represents a purchase receipt.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientPurchaseReceipt
    {
        /// <summary>Product ID.</summary>
        public string productId;
        /// <summary>Order ID.</summary>
        public int orderId;
        /// <summary>Invoice ID.</summary>
        public string invoiceId;
        /// <summary>Transaction ID.</summary>
        public string transactionId;
        /// <summary>Receipt string.</summary>
        public string receipt;
        /// <summary>Developer payload.</summary>
        public string developerPayload;

        /// <summary>Order status.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public XsollaStoreClientPurchasedProduct.Status orderStatus = XsollaStoreClientPurchasedProduct.Status.Unknown;

        /// <summary>
        /// Builder for <see cref="XsollaStoreClientPurchaseReceipt"/>.
        /// </summary>
        public class Builder
        {
            private readonly XsollaStoreClientPurchaseReceipt _purchaseReceipt = new XsollaStoreClientPurchaseReceipt();

            /// <summary>
            /// Creates a new builder instance.
            /// </summary>
            public static Builder Create() => new Builder();

            /// <summary>
            /// Sets the product ID.
            /// </summary>
            /// <param name="productId">Product ID.</param>
            public Builder SetProductId(string productId) { _purchaseReceipt.productId = productId; return this; }

            /// <summary>
            /// Sets the order ID.
            /// </summary>
            /// <param name="orderId">Order ID.</param>
            public Builder SetOrderId(int orderId) { _purchaseReceipt.orderId = orderId; return this; }

            /// <summary>
            /// Sets the invoice ID.
            /// </summary>
            /// <param name="invoiceId">Invoice ID.</param>
            public Builder SetInvoiceId(string invoiceId) { _purchaseReceipt.invoiceId = invoiceId; return this; }

            /// <summary>
            /// Sets the transaction ID.
            /// </summary>
            /// <param name="transactionId">Transaction ID.</param>
            public Builder SetTransactionId(string transactionId) { _purchaseReceipt.transactionId = transactionId; return this; }

            /// <summary>
            /// Sets the receipt.
            /// </summary>
            /// <param name="receipt">Receipt string.</param>
            public Builder SetReceipt(string receipt) { _purchaseReceipt.receipt = receipt; return this; }

            /// <summary>
            /// Sets the order status.
            /// </summary>
            /// <param name="orderStatus">Order status.</param>
            public Builder SetOrderStatus(XsollaStoreClientPurchasedProduct.Status orderStatus) { _purchaseReceipt.orderStatus = orderStatus; return this; }

            /// <summary>
            /// Sets the developer payload.
            /// </summary>
            /// <param name="developerPayload">Developer payload string.</param>
            public Builder SetDeveloperPayload(string developerPayload) { _purchaseReceipt.developerPayload = developerPayload; return this; }

            /// <summary>
            /// Builds the purchase receipt instance.
            /// </summary>
            public XsollaStoreClientPurchaseReceipt Build() => _purchaseReceipt;
        }

        /// <summary>
        /// Serializes the purchase receipt to JSON.
        /// </summary>
        /// <returns>JSON string of the receipt.</returns>
        public string ToJson() => XsollaStoreClientHelpers.ReceiptToJson(this);
    }

    /// <summary>
    /// Result for validating a purchase.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientValidatePurchaseResult
    {
        /// <summary>
        /// Indicates if the purchase is valid.
        /// </summary>
        public bool success;
    }

    /// <summary>
    /// Result containing access token.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientAccessTokenResult
    {
        /// <summary>
        /// Access token string.
        /// </summary>
        public string token;
    }

    /// <summary>
    /// Result containing storefront identifier.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientStorefrontResult
    {
        /// <summary>
        /// Storefront identifier string.
        /// </summary>
        public string storefront;
    }
    
    /// <summary>
    /// Result containing whether app is running in alternative distribution.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientDistributionResult
    {
        /// <summary>
        /// Whether app is running in alternative distribution.
        /// </summary>
        public bool isRunningInAlternativeDistribution;
    }

    /// <summary>
    /// Payment data for store client.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientPaymentData
    {
        /// <summary>Product SKU.</summary>
        public string sku;
        /// <summary>Developer payload.</summary>
        public string developerPayload;
        /// <summary>External ID.</summary>
        public string externalId;

        /// <summary>Payment token (optional).</summary>
        [CanBeNull] public string paymentToken;
        public bool allowTokenOnlyFinishedStatusWithoutOrderId = false;

        /// <summary>Payment method ID (optional).</summary>
        public int? paymentMethodId;

        /// <summary>
        /// Initializes a new instance of <see cref="XsollaStoreClientPaymentData"/>.
        /// </summary>
        /// <param name="sku">Product SKU.</param>
        /// <param name="developerPayload">Developer payload.</param>
        /// <param name="externalId">External ID.</param>
        /// <param name="paymentToken">Payment token (optional).</param>
        /// <param name="paymentMethodId">Payment method ID (optional).</param>
        public XsollaStoreClientPaymentData(
            string sku, string developerPayload, string externalId,
            [CanBeNull] string paymentToken = null, int? paymentMethodId = null, 
            bool allowTokenOnlyFinishedStatusWithoutOrderId = false
        )
        {
            this.sku = sku;
            this.developerPayload = developerPayload;
            this.externalId = externalId;
            this.paymentToken = paymentToken;
            this.paymentMethodId = paymentMethodId;
            this.allowTokenOnlyFinishedStatusWithoutOrderId = allowTokenOnlyFinishedStatusWithoutOrderId;
        }
    }

    /// <summary>
    /// Data for consuming a product.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientConsumeData
    {
        /// <summary>Product SKU.</summary>
        public string sku;
        /// <summary>Quantity to consume.</summary>
        public int quantity;
        /// <summary>Transaction ID.</summary>
        public string transactionId;
        /// <summary>Receipt string.</summary>
        public string receipt;

        /// <summary>
        /// Initializes a new instance of <see cref="XsollaStoreClientConsumeData"/>.
        /// </summary>
        /// <param name="sku">Product SKU.</param>
        /// <param name="quantity">Quantity to consume.</param>
        /// <param name="transactionId">Transaction ID.</param>
        /// <param name="receipt">Receipt string.</param>
        public XsollaStoreClientConsumeData(string sku, int quantity, string transactionId, string receipt)
        {
            this.sku = sku;
            this.quantity = quantity;
            this.transactionId = transactionId;
            this.receipt = receipt;
        }
    }

    /// <summary>
    /// Store client settings asset.
    /// </summary>
    public class XsollaStoreClientSettingsAsset : XsollaClientSettingsAsset
    {
        /// <summary>
        /// Gets the singleton instance of the settings asset.
        /// </summary>
        public new static XsollaClientSettingsAsset Instance() => XsollaClientSettingsAsset.Instance();
    }

    /// <summary>
    /// Store client settings.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientSettings : XsollaClientSettings
    {
        /// <summary>
        /// Builder for <see cref="XsollaStoreClientSettings"/>.
        /// </summary>
        public new class Builder : XsollaClientSettings.Builder
        {
            /// <summary>
            /// Creates a new builder instance.
            /// </summary>
            public new static Builder Create() => new Builder();

            /// <summary>
            /// Updates the builder with existing settings.
            /// </summary>
            /// <param name="settings">Existing settings.</param>
            public new static Builder Update(XsollaClientSettings settings)
            {
                var builder = new Builder();
                builder._settings = settings;
                return builder;
            }
        }
    }

    /// <summary>
    /// Store client configuration.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientConfiguration : XsollaClientConfiguration
    {
        /// <summary>
        /// Builder for <see cref="XsollaStoreClientConfiguration"/>.
        /// </summary>
        public new class Builder : XsollaClientConfiguration.Builder
        {
            /// <summary>
            /// Creates a new builder instance.
            /// </summary>
            public new static Builder Create() => new Builder();

            /// <summary>
            /// Updates the builder with existing configuration.
            /// </summary>
            /// <param name="configuration">Existing configuration.</param>
            public new static Builder Update(XsollaClientConfiguration configuration)
            {
                var builder = new Builder();
                builder._configuration = configuration;
                return builder;
            }
        }
    }

    /// <summary>
    /// Arguments for store client purchase.
    /// </summary>
    public class XsollaStoreClientPurchaseArgs
    {
        /// <summary>External ID.</summary>
        public string externalId = string.Empty;
        /// <summary>Payment token.</summary>
        public string paymentToken = string.Empty;
        
        public bool allowTokenOnlyFinishedStatusWithoutOrderId = false;

        /// <summary>Developer payload (optional).</summary>
        [CanBeNull] public string developerPayload = null;

        /// <summary>Payment method ID (optional).</summary>
        public int? paymentMethodId = null;

        /// <summary>
        /// Gets an empty purchase arguments instance.
        /// </summary>
        public static XsollaStoreClientPurchaseArgs Empty => Builder.Create().Build();

        public class Builder
        {
            private XsollaStoreClientPurchaseArgs _args = new XsollaStoreClientPurchaseArgs();

            /// <summary>
            /// Creates a new builder instance.
            /// </summary>
            public static Builder Create() => new Builder();

            public Builder SetExternalId(string externalId)  { _args.externalId = externalId; return this; }
            public Builder SetPaymentToken(string paymentToken)  { _args.paymentToken = paymentToken; return this; }
            public Builder SetDeveloperPayload([CanBeNull] string developerPayload)  { _args.developerPayload = developerPayload; return this; }
            public Builder SetPaymentMethodId(int? paymentMethodId)  { _args.paymentMethodId = paymentMethodId; return this; }
            
            [Obsolete("Deprecated since v3.1.1. Will be removed in a future major version.")]
            public Builder SetAllowTokenOnlyFinishedStatusWithoutOrderId(bool allowTokenOnlyFinishedStatusWithoutOrderId)  { _args.allowTokenOnlyFinishedStatusWithoutOrderId = allowTokenOnlyFinishedStatusWithoutOrderId; return this; }

            public XsollaStoreClientPurchaseArgs Build() => _args;
        }

    }
}