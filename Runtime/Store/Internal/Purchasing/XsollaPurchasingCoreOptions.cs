using System;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// Behaviours of <see cref="XsollaPurchasingCore"/> that only one Unity IAP line needs, chosen by the binding that
    /// constructs it.
    /// <para/>
    /// Unity IAP 4.x completes purchases that exist at startup from the receipts on the products it retrieves, so its
    /// binding turns both startup behaviours on; a binding that fetches purchases separately through
    /// <see cref="XsollaPurchasingCore.FetchPurchases"/> turns both off.
    /// </summary>
    internal sealed class XsollaPurchasingCoreOptions
    {
        /// <summary>
        /// Whether the first <see cref="XsollaPurchasingCore.FetchProducts"/> after
        /// <see cref="XsollaPurchasingCore.Initialize"/> also restores the user's unconsumed purchases and hands each
        /// one over as <see cref="XsollaPurchasingProduct.restoredPurchase"/>; a failed restore then fails that fetch.
        /// </summary>
        public readonly bool restoresWithFirstFetch;

        /// <summary>
        /// Whether a purchase reported before the first <see cref="XsollaPurchasingCore.FetchProducts"/> after
        /// <see cref="XsollaPurchasingCore.Initialize"/> is dropped instead of waiting for that fetch.
        /// <para/>
        /// Without the restore piggyback, dropping would lose an unprompted native delivery that arrives before the
        /// first fetch until the next purchases fetch.
        /// </summary>
        public readonly bool dropsPurchasesBeforeFirstFetch;

        /// <summary>
        /// Whether a purchase delivered through <see cref="IXsollaPurchasingCoreCallback.OnPurchaseSucceeded"/> is also
        /// delivered at most once per order and SKU in a session, on top of once per transaction and SKU; a purchase
        /// without an order id (0) is deduplicated by transaction only.
        /// <para/>
        /// One order can reach the core under two transaction ids: the store clients make up a random transaction id
        /// when the platform reports none, so an order reported twice, such as by a Standalone payment event and order
        /// event, or by an Android result fanned out to another request, would otherwise be delivered twice. Off for
        /// Unity IAP 4.x, whose deduplication stays by transaction only.
        /// </summary>
        public readonly bool deduplicatesByOrderId;

        /// <summary>
        /// Whether a restore delivers an identity-less purchase, one flagged
        /// <see cref="XsollaStoreClientPurchasedProduct.identityless"/> or without an order id or a receipt, only for the units
        /// of its SKU beyond those handed over and not consumed in this session and those of purchases of the SKU
        /// waiting for a result their store client can answer (<see cref="IXsollaStoreClient.CanAnswerPurchase"/>).
        /// <para/>
        /// The Standalone inventory restore reports each owned unit under a transaction id it makes up on every restore,
        /// so neither the transaction nor the order dedup sees that two restores reported the same unit, and a unit
        /// handed over and not confirmed yet would be delivered again. The Android inventory restore keeps the ids it
        /// makes up for a session, but reports a unit a purchase result delivered under one of them, with no order id. A unit handed over on its product by the first
        /// <see cref="XsollaPurchasingCore.FetchProducts"/> counts as handed over too. The inventory also credits a
        /// purchased unit at payment, before the order tracker answers the purchase, whose result delivers it again.
        /// </summary>
        public readonly bool limitsIdentitylessRestores;

        /// <summary>
        /// Whether a restored purchase the Android inventory restore reports under a made-up id
        /// (<see cref="XsollaStoreClientPurchasedProduct.identityless"/>) is delivered under that native id, as an
        /// ordinary purchase, when the product types passed to <see cref="XsollaPurchasingCore.FetchProducts"/> make its
        /// SKU a <see cref="XsollaPurchasingProductType.NonConsumable"/> or a
        /// <see cref="XsollaPurchasingProductType.Subscription"/>.
        /// <para/>
        /// Unity IAP 4.x keeps a transaction log across launches and finishes a purchase under a logged transaction id
        /// without granting it again. An owned non-consumable is never consumed, so every launch restores it at the same
        /// position under the same native id, while the id <see cref="XsollaRestoredTransactionIdsAndroid"/> gives it
        /// is new every launch and would grant it on each. A consumable keeps that id, as a consume moves the next unit
        /// onto the native id of one already granted, and so does a SKU of no known type. A holding this device bought
        /// is put back under the purchase's transaction id instead of the native id, as Unity IAP logged that one at the
        /// purchase, when the record passed to the core holds it (see
        /// <see cref="XsollaNonConsumableTransactionIdsAndroid"/>). Never with <see
        /// cref="limitsIdentitylessRestores"/>, which holds back only flagged purchases: a non-consumable bought in the
        /// session would be delivered again by a restore under its native id.
        /// </summary>
        public readonly bool restoresNonConsumablesUnderNativeIds;

        /// <summary>
        /// Initializes a new instance of the <see cref="XsollaPurchasingCoreOptions"/> class.
        /// </summary>
        /// <param name="restoresWithFirstFetch">Whether the first catalog fetch also restores purchases.</param>
        /// <param name="dropsPurchasesBeforeFirstFetch">Whether a purchase reported before the first catalog fetch is dropped.</param>
        /// <param name="deduplicatesByOrderId">Whether a purchase is also delivered at most once per order and SKU.</param>
        /// <param name="limitsIdentitylessRestores">Whether a restore delivers an identity-less purchase only for the units not handed over yet.</param>
        /// <param name="restoresNonConsumablesUnderNativeIds">Whether a restored non-consumable keeps the id the Android SDK made up for it.</param>
        /// <exception cref="ArgumentException">
        /// Both <paramref name="limitsIdentitylessRestores"/> and <paramref name="restoresNonConsumablesUnderNativeIds"/> are on.
        /// </exception>
        public XsollaPurchasingCoreOptions(
            bool restoresWithFirstFetch,
            bool dropsPurchasesBeforeFirstFetch,
            bool deduplicatesByOrderId = false,
            bool limitsIdentitylessRestores = false,
            bool restoresNonConsumablesUnderNativeIds = false
        )
        {
            if (limitsIdentitylessRestores && restoresNonConsumablesUnderNativeIds)
            {
                throw new ArgumentException(
                    "restoresNonConsumablesUnderNativeIds cannot be combined with limitsIdentitylessRestores",
                    nameof(restoresNonConsumablesUnderNativeIds)
                );
            }

            this.restoresWithFirstFetch = restoresWithFirstFetch;
            this.dropsPurchasesBeforeFirstFetch = dropsPurchasesBeforeFirstFetch;
            this.deduplicatesByOrderId = deduplicatesByOrderId;
            this.limitsIdentitylessRestores = limitsIdentitylessRestores;
            this.restoresNonConsumablesUnderNativeIds = restoresNonConsumablesUnderNativeIds;
        }

        public override string ToString() =>
            $"Options [restoresWithFirstFetch={restoresWithFirstFetch}, dropsPurchasesBeforeFirstFetch={dropsPurchasesBeforeFirstFetch}, deduplicatesByOrderId={deduplicatesByOrderId}, limitsIdentitylessRestores={limitsIdentitylessRestores}, restoresNonConsumablesUnderNativeIds={restoresNonConsumablesUnderNativeIds}]";
    }
}
