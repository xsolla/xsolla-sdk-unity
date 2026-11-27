using System;
using System.Collections.Generic;
using System.Globalization;
using Xsolla.SDK.Common;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// Gives each unit the Android SDK's inventory restore reports under a made-up id a transaction id of its own, stable
    /// for the session and never reused across sessions.
    /// <para/>
    /// The Android SDK makes up one id per position in a SKU's quantity, used as order, invoice and transaction id
    /// alike, and a consume moves the next unit into the freed position and id, so Unity IAP 4.x's transaction log
    /// finishes a unit it never granted. Such a row gets a random transaction id, no order id and the
    /// <see cref="XsollaStoreClientPurchasedProduct.identityless"/> flag; the rest of the row is kept, so consuming and
    /// validating it work as before.
    /// <para/>
    /// The units of a SKU are interchangeable, so its ids form an ordered list: the row at position <c>i</c> takes the
    /// <c>i</c>th id, and a consumed id leaves the list. A repeated restore reports the units still held under the same
    /// ids, which the dedup of <see cref="XsollaPurchasingCore"/> drops; a new session starts with no list, so a unit
    /// granted and not consumed before the app quit is granted again: a duplicate, never a loss. A non-consumable gets an
    /// id too, and for a Unity IAP line that needs it
    /// (<see cref="XsollaPurchasingCoreOptions.restoresNonConsumablesUnderNativeIds"/>) the core puts back the one Unity
    /// IAP's transaction log holds for it: the transaction id of the purchase it recorded for the holding, or else the
    /// native id.
    /// </summary>
    internal sealed class XsollaRestoredTransactionIdsAndroid
    {
        private const string Tag = "XsollaRestoredTransactionIdsAndroid";

        /// <summary>
        /// A transaction id handed out for a position of a SKU, and whether the unit it was handed over with is consumed.
        /// </summary>
        private sealed class Slot
        {
            public readonly string transactionId;

            /// <summary>
            /// The number of restores sent when the consume of the unit succeeded, or null while it is held. The restores
            /// numbered below it were sent before the consume succeeded.
            /// </summary>
            public readonly long? restoresSentAtConsume;

            private Slot(string transactionId, long? restoresSentAtConsume)
            {
                this.transactionId = transactionId;
                this.restoresSentAtConsume = restoresSentAtConsume;
            }

            public static Slot Held(string transactionId) => new Slot(transactionId, null);

            public Slot ConsumedWhen(long restoresSent) => new Slot(transactionId, restoresSent);

            public bool IsHeld => restoresSentAtConsume == null;

            /// <summary>
            /// Whether the rows of <paramref name="restore"/> map onto this slot: it is held, or consumed after the
            /// restore was sent, so the restore may have read the inventory before the consume took effect.
            /// </summary>
            public bool IsVisibleTo(long restore) => restoresSentAtConsume == null || restore < restoresSentAtConsume;
        }

        /// <summary>
        /// The slots handed out for the units of one SKU, in position order, and the made-up id of the position they
        /// started from.
        /// <para/>
        /// The made-up id of a SKU's first position is derived from the project and the user the access token names, so
        /// it anchors the list: a restore whose first row of the SKU carries another id reads another user's inventory and
        /// maps onto that user's list, a new one the first time. Every user's list is kept for the session, so a switch
        /// back to a user reports the units that user still holds under the ids they were handed over with. A token that
        /// names no user leaves the project alone to derive the anchor from, so every user shares the lists, as they share
        /// the Android SDK's own ids, and a user change goes unseen.
        /// </summary>
        private sealed class Holding
        {
            public readonly long anchorId;

            public readonly List<Slot> slots = new List<Slot>();

            public Holding(long anchorId)
            {
                this.anchorId = anchorId;
            }
        }

        /// <summary>
        /// The lists of every SKU, one per anchor, so one per user who restored it this session.
        /// </summary>
        private readonly Dictionary<string, List<Holding>> _holdingsBySku = new Dictionary<string, List<Holding>>();

        /// <summary>
        /// The number of restores sent, which numbers the next one.
        /// </summary>
        private long _restoresSent;

        /// <summary>
        /// The numbers of the restores sent and not answered yet.
        /// </summary>
        private readonly HashSet<long> _restoresWaiting = new HashSet<long>();

        /// <summary>
        /// Whether <paramref name="product"/> is a restored row the Android SDK made up its ids for: one it reports as an
        /// inventory restore.
        /// <para/>
        /// A row of unknown origin, from a bridge or Android SDK that reports none, is told by its shape: its invoice and
        /// order id are one and the same number, and it reports no payment time. A live purchase carries two different
        /// real ids, and one restored from purchase events reports its payment time.
        /// </summary>
        private static bool IsMadeUp(XsollaStoreClientPurchasedProduct product)
        {
            if (product.origin == XsollaStoreClientPurchasedProduct.Origin.InventoryRestore)
            {
                return true;
            }
            else if (product.origin == XsollaStoreClientPurchasedProduct.Origin.Unknown)
            {
                return product.status == XsollaStoreClientPurchasedProduct.Status.Restored
                    && product.orderId > 0
                    && product.invoiceId == product.orderId.ToString(CultureInfo.InvariantCulture)
                    && product.purchaseTimeMillis == 0;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Numbers a restore about to be sent to the Android SDK, which reads the inventory some time after.
        /// </summary>
        /// <returns>The restore's number, for <see cref="Assign"/> or <see cref="OnRestoreFailed"/>.</returns>
        public long OnRestoreSent()
        {
            var restore = _restoresSent++;
            _restoresWaiting.Add(restore);
            return restore;
        }

        /// <summary>
        /// Rewrites the made-up rows the restore numbered <paramref name="restore"/> answered with, in the order the
        /// Android SDK reports them, and returns every row, the others as they were. A collapsed row of several units is
        /// one position and takes one id.
        /// </summary>
        /// <param name="restore">The number <see cref="OnRestoreSent"/> gave the restore.</param>
        /// <param name="restored">The rows the restore reported.</param>
        /// <returns>The rows to hand over, in the same order.</returns>
        public XsollaStoreClientPurchasedProduct[] Assign(long restore, XsollaStoreClientPurchasedProduct[] restored)
        {
            _restoresWaiting.Remove(restore);

            var nextSlotBySku = new Dictionary<string, (Holding holding, int slot)>();

            var rows = restored.Map(product =>
            {
                if (!IsMadeUp(product))
                {
                    return product;
                }
                else
                {
                    var sku = product.sku ?? string.Empty;
                    var (holding, slot) = nextSlotBySku.TryGetValue(sku, out var next)
                        ? next
                        : (HoldingAnchoredAt(sku, product.orderId), 0);

                    while (slot < holding.slots.Count && !holding.slots[slot].IsVisibleTo(restore))
                        slot++;

                    if (slot == holding.slots.Count)
                        holding.slots.Add(Slot.Held(Guid.NewGuid().ToString()));

                    var transactionId = holding.slots[slot].transactionId;
                    nextSlotBySku[sku] = (holding, slot + 1);

                    XsollaLogger.Debug(Tag, $"Restored unit of a made-up id: sku={product.sku} restore={restore} slot={slot} consumed={!holding.slots[slot].IsHeld} nativeTransactionId={product.transactionId} transactionId={transactionId}");

                    return XsollaStoreClientPurchasedProduct.Builder.Create()
                        .SetSku(product.sku)
                        .SetOrderId(0)
                        .SetInvoiceId(product.invoiceId)
                        .SetTransactionId(transactionId)
                        .SetReceipt(product.receipt)
                        .SetQuantity(product.quantity)
                        .SetPurchaseTimeMillis(product.purchaseTimeMillis)
                        .SetStatus(product.status)
                        .SetOrigin(product.origin)
                        .SetDeveloperPayload(product.developerPayload)
                        .SetIdentityless(true)
                        .Build();
                }
            });

            RemoveConsumedSlotsNoRestoreWaits();
            return rows;
        }

        /// <summary>
        /// Notes that the restore numbered <paramref name="restore"/> failed, or that its answer is dropped, so that it
        /// no longer holds consumed ids in their positions.
        /// </summary>
        /// <param name="restore">The number <see cref="OnRestoreSent"/> gave the restore.</param>
        public void OnRestoreFailed(long restore)
        {
            _restoresWaiting.Remove(restore);
            RemoveConsumedSlotsNoRestoreWaits();
        }

        /// <summary>
        /// Marks the id of a unit whose consume succeeded as consumed, so the units behind it move up a position each and
        /// keep their ids for the restores sent from now on; those sent before still see it in its position. An id this
        /// instance never handed out, or marked already, changes nothing.
        /// <para/>
        /// A restore sent before the consume succeeded may have read the inventory before the consume took effect and
        /// report the consumed unit as well; keeping the consumed id in its position maps its rows onto the ids they were
        /// handed over with, which the dedup drops. Closing the list up at once would shift its last row onto a new id, a
        /// unit Unity IAP 4.x grants again. If such a restore read the inventory after the consume took effect, a held or
        /// new unit shifts onto the consumed id instead, and its delivery waits for a later restore or launch. Native
        /// answers a restore sent while another purchases query is pending with that query's answer, so even a restore
        /// sent after the consume may report the consumed unit: its last row then maps onto a new id and is granted
        /// again, a duplicate, never a loss.
        /// </summary>
        /// <param name="sku">The consumed SKU.</param>
        /// <param name="transactionId">The consumed unit's transaction id.</param>
        public void OnConsumed(string sku, string transactionId)
        {
            if (_holdingsBySku.TryGetValue(sku ?? string.Empty, out var holdings))
            {
                foreach (var holding in holdings)
                {
                    var slot = holding.slots.FindIndex(candidate => candidate.IsHeld && candidate.transactionId == transactionId);
                    if (slot >= 0)
                    {
                        holding.slots[slot] = holding.slots[slot].ConsumedWhen(_restoresSent);
                        XsollaLogger.Debug(Tag, $"Consumed unit leaves the restored ids: sku={sku} transactionId={transactionId} restoresSent={_restoresSent} restoresWaiting={_restoresWaiting.Count}");
                        RemoveConsumedSlotsNoRestoreWaits();
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Takes out the consumed slots no restore waiting for its answer sees, that is, whose consume succeeded before
        /// every waiting restore was sent.
        /// </summary>
        private void RemoveConsumedSlotsNoRestoreWaits()
        {
            var oldestWaiting = long.MaxValue;
            foreach (var restore in _restoresWaiting)
                oldestWaiting = Math.Min(oldestWaiting, restore);

            foreach (var holdings in _holdingsBySku.Values)
            {
                foreach (var holding in holdings)
                    holding.slots.RemoveAll(slot => !slot.IsVisibleTo(oldestWaiting));
            }
        }

        /// <summary>
        /// The list of <paramref name="sku"/> anchored at <paramref name="anchorId"/>, a new one when there is none: the
        /// first restore of the SKU by the user the anchor stands for.
        /// </summary>
        private Holding HoldingAnchoredAt(string sku, long anchorId)
        {
            if (!_holdingsBySku.TryGetValue(sku, out var holdings))
            {
                holdings = new List<Holding>();
                _holdingsBySku[sku] = holdings;
            }

            var holding = holdings.Find(candidate => candidate.anchorId == anchorId);
            if (holding == null)
            {
                if (holdings.Count > 0)
                    XsollaLogger.Debug(Tag, $"Restored ids of sku={sku} start a list anchored at {anchorId}: another user's inventory");

                holding = new Holding(anchorId);
                holdings.Add(holding);
            }

            return holding;
        }
    }
}
