using JetBrains.Annotations;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// A catalog entry already mapped to the metadata Unity IAP shows for a product.
    /// <para/>
    /// Mapping in the core, rather than in each binding, keeps the choice of price fields in one place.
    /// </summary>
    internal sealed class XsollaPurchasingProduct
    {
        /// <summary>SKU of the product; Unity IAP's store-specific id.</summary>
        public readonly string sku;

        /// <summary>Localized title.</summary>
        public readonly string title;

        /// <summary>Localized description.</summary>
        public readonly string description;

        /// <summary>Formatted, localized price.</summary>
        public readonly string priceString;

        /// <summary>ISO 4217 currency code of <see cref="price"/>.</summary>
        public readonly string currencyCode;

        /// <summary>Price in major currency units; the store client reports micros.</summary>
        public readonly decimal price;

        /// <summary>
        /// An unconsumed purchase of this product restored together with the catalog, or null.
        /// <para/>
        /// Set only on the first catalog fetch and only with
        /// <see cref="XsollaPurchasingCoreOptions.restoresWithFirstFetch"/>, for a binding that completes purchases
        /// existing at startup from receipts on the products it retrieves (Unity IAP 4.x). Always null otherwise.
        /// </summary>
        [CanBeNull] public readonly XsollaPurchasingPurchase restoredPurchase;

        /// <summary>
        /// Initializes a new instance of the <see cref="XsollaPurchasingProduct"/> class.
        /// </summary>
        /// <param name="sku">SKU of the product.</param>
        /// <param name="title">Localized title.</param>
        /// <param name="description">Localized description.</param>
        /// <param name="priceString">Formatted, localized price.</param>
        /// <param name="currencyCode">ISO 4217 currency code.</param>
        /// <param name="price">Price in major currency units.</param>
        /// <param name="restoredPurchase">An unconsumed purchase restored together with the catalog, or null.</param>
        public XsollaPurchasingProduct(
            string sku,
            string title,
            string description,
            string priceString,
            string currencyCode,
            decimal price,
            [CanBeNull] XsollaPurchasingPurchase restoredPurchase
        )
        {
            this.sku = sku;
            this.title = title;
            this.description = description;
            this.priceString = priceString;
            this.currencyCode = currencyCode;
            this.price = price;
            this.restoredPurchase = restoredPurchase;
        }

        public override string ToString() =>
            $"Product [sku='{sku}', price={price} {currencyCode}, restoredPurchase={restoredPurchase?.ToString() ?? "none"}]";
    }
}
