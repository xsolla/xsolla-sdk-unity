using System.Collections.Generic;
using JetBrains.Annotations;
using Xsolla.SDK.Common;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// Tells where a purchase the iOS bridge delivers came from, until the iOS SDK reports it.
    /// <para/>
    /// The bridge sends every purchase as paid, with the order id as transaction id, or an empty one when there is no
    /// order behind it. A purchase answering a purchase call of this session is live. One the default listener delivers
    /// is judged by its transaction id: under the Events API the rule mirrors native Android's Events origin rule,
    /// otherwise an empty id is an inventory restore and anything else is unknown.
    /// <para/>
    /// The bridge hands each transaction to exactly one of the two: to the purchase call that started it, or else to the
    /// listener. Both answer on the main thread in the order they arrive, so the id of a purchase call's answer is
    /// recorded before the listener can deliver the same order again.
    /// </summary>
    internal sealed class XsollaPurchaseOriginsIOS
    {
        private readonly XsollaClientSettings.WebhooksMode _webhooksMode;

        private readonly bool _localPurchasesRestore;

        /// <summary>
        /// The order ids this session's purchase calls were answered with, each until the listener delivers it. Kept in
        /// memory only, as native Android keeps its invoice ids.
        /// </summary>
        private readonly HashSet<string> _purchaseCallIds = new HashSet<string>();

        public XsollaPurchaseOriginsIOS(XsollaClientSettings.WebhooksMode webhooksMode, bool localPurchasesRestore)
        {
            _webhooksMode = webhooksMode;
            _localPurchasesRestore = localPurchasesRestore;
        }

        /// <summary>
        /// Records the order id of a purchase call's answer, and says it is live.
        /// </summary>
        /// <param name="purchaseJson">The purchase as the bridge sent it.</param>
        public XsollaStoreClientPurchasedProduct.Origin OfPurchaseCallAnswer(string purchaseJson)
        {
            var transactionId = TransactionIdOf(purchaseJson);
            if (!string.IsNullOrEmpty(transactionId))
                _purchaseCallIds.Add(transactionId);

            return XsollaStoreClientPurchasedProduct.Origin.Live;
        }

        /// <summary>
        /// Says where a purchase the default listener delivers came from.
        /// </summary>
        /// <param name="purchaseJson">The purchase as the bridge sent it.</param>
        public XsollaStoreClientPurchasedProduct.Origin OfListenerPurchase(string purchaseJson)
        {
            var transactionId = TransactionIdOf(purchaseJson);
            XsollaStoreClientPurchasedProduct.Origin origin;

            if (transactionId == null)
            {
                origin = XsollaStoreClientPurchasedProduct.Origin.Unknown;
            }
            else if (_webhooksMode == XsollaClientSettings.WebhooksMode.EventsApi)
            {
                origin = OfEventsListenerPurchase(transactionId);
            }
            else if (transactionId.Length == 0)
            {
                origin = XsollaStoreClientPurchasedProduct.Origin.InventoryRestore;
            }
            else
            {
                origin = XsollaStoreClientPurchasedProduct.Origin.Unknown;
            }

            return origin;
        }

        /// <summary>
        /// Mirrors native Android's Events origin rule: an order this session's purchase call started is live, any other
        /// order is an Events API restore, and a purchase with no order, a free grant, is live.
        /// <para/>
        /// Unlike Android, the iOS bridge also leaves the inventory restore on under the Events API when
        /// <c>localPurchasesRestore</c> is set, and that restore has no order either. With it set, a purchase with no
        /// order is unknown.
        /// </summary>
        private XsollaStoreClientPurchasedProduct.Origin OfEventsListenerPurchase(string transactionId)
        {
            XsollaStoreClientPurchasedProduct.Origin origin;

            if (transactionId.Length == 0)
            {
                origin = _localPurchasesRestore
                    ? XsollaStoreClientPurchasedProduct.Origin.Unknown
                    : XsollaStoreClientPurchasedProduct.Origin.Live;
            }
            else if (_purchaseCallIds.Remove(transactionId))
            {
                origin = XsollaStoreClientPurchasedProduct.Origin.Live;
            }
            else
            {
                origin = XsollaStoreClientPurchasedProduct.Origin.EventsRestore;
            }

            return origin;
        }

        /// <summary>
        /// The transaction id the bridge sent, empty when the purchase has no order, or null when the JSON holds no
        /// purchase. Read from the JSON because the purchase built from it carries a made-up id in place of an empty one.
        /// </summary>
        [CanBeNull]
        private static string TransactionIdOf(string purchaseJson)
        {
            var purchases = XsollaClientHelpers.FromJson<XsollaStoreClientPurchasedProductResult>(purchaseJson)?.purchases;

            return purchases != null && purchases.Length > 0 ? purchases[0]?.transactionId : null;
        }
    }
}
