using System;
using System.Collections.Generic;
using Xsolla.SDK.Common;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// The purchase token per (transaction, sku) the Android store client consumes with, as a <see cref="ReceiptEntry"/>,
    /// and the consume that reads it.
    /// <para/>
    /// Keyed by sku as well, not by transaction id alone: bundle and virtual-currency purchases are delivered as several
    /// content lines that share one transaction id but each carry a distinct receipt, so a transaction-only key lets one
    /// line's token overwrite another's.
    /// <para/>
    /// The Android SDK consumes by SKU, whatever token it is given, so a native consume of a line consumed already spends
    /// another held unit of the SKU, one nobody may have been granted.
    /// </summary>
    internal sealed class XsollaReceiptCacheAndroid
    {
        private const string Tag = "XsollaReceiptCacheAndroid";

        /// <summary>
        /// Cache value for one purchased content line. An explicit state model that replaces a nullable
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

                /// <summary>
                /// The units a restored row holds, or zero for a purchase result. A restored unit's token moves with its
                /// position, so a changed token is expected for it.
                /// </summary>
                public readonly int RestoredQuantity;

                public Pending(string receipt, int restoredQuantity)
                {
                    Receipt = receipt ?? "";
                    RestoredQuantity = restoredQuantity;
                }
            }

            /// <summary>
            /// Already consumed this session, so a consume repeated for it resolves as an idempotent success
            /// rather than spending a token the native bridge has already consumed. No delivery turns it back to
            /// <see cref="Pending"/>; see <see cref="XsollaReceiptCacheAndroid.Add"/>.
            /// </summary>
            public sealed class Consumed : ReceiptEntry
            {
                public static readonly Consumed Instance = new();

                private Consumed() { }
            }

            /// <summary>
            /// A consume of it ended with an outcome that could not be resolved
            /// (<see cref="XsollaConsumeOutcomeCheckAndroid.Verdict.Unresolved"/>), so it is never consumed again this
            /// session, not even after a fresh delivery: its units come back with the next launch's restore rather
            /// than a retry consuming a unit the user still owns.
            /// </summary>
            public sealed class Refused : ReceiptEntry
            {
                public static readonly Refused Instance = new();

                private Refused() { }
            }

            private ReceiptEntry() { }
        }

        private readonly Dictionary<(string transactionId, string sku), ReceiptEntry> _entries =
            new Dictionary<(string transactionId, string sku), ReceiptEntry>(capacity: 4);

        /// <summary>
        /// Caches the token of a delivered content line until its consume.
        /// <para/>
        /// A line consumed this session stays consumed whatever delivers it again. A real order's id is never reused, and
        /// the units the Android SDK reports under ids made up from their position get transaction ids of their own
        /// (<see cref="XsollaRestoredTransactionIdsAndroid"/>), so a delivery meets a consumed line only as a repeat: a
        /// purchase result delivered again for its order, or a restored row in the answer of a restore sent before the
        /// consume succeeded, carrying the consumed unit read before the consume took effect or a held unit moved onto
        /// its id. Were the line pending again, a consume repeated for its transaction would spend another held unit of
        /// the SKU. <c>XsollaPurchasingStore</c> drops such a restored row and absorbs repeated finishes on the Unity
        /// IAP paths, so this guards integrators calling <see cref="XsollaStoreClient.ConsumeProduct"/> directly, and
        /// is defence in depth under <c>XsollaPurchasingStore</c>.
        /// </summary>
        /// <param name="restoredQuantity">The units a restored row holds, or zero for a purchase result.</param>
        public void Add(string transactionId, string sku, string receipt, int restoredQuantity)
        {
            var key = (transactionId, sku);
            _entries.TryGetValue(key, out var existing);

            if (existing is ReceiptEntry.Consumed)
            {
                XsollaLogger.Debug(Tag, $"Repeat delivery of a purchase consumed this session, it stays consumed (transactionId={transactionId} sku={sku} restoredQuantity={restoredQuantity})");
            }
            else if (existing is ReceiptEntry.Refused)
            {
                XsollaLogger.Debug(Tag, $"Keeping a refused purchase refused over a fresh delivery (transactionId={transactionId} sku={sku} restoredQuantity={restoredQuantity})");
            }
            else
            {
                // Only warn when overwriting a still-`Pending` purchase whose non-empty token differs: a restored unit
                // whose position, and with it its token, moved up once a unit ahead of it was consumed is expected.
                if (existing is ReceiptEntry.Pending existingPending &&
                    !string.IsNullOrEmpty(existingPending.Receipt) &&
                    existingPending.Receipt != receipt &&
                    (existingPending.RestoredQuantity == 0 || restoredQuantity == 0)) {
                    XsollaLogger.Warning(Tag, "Cache already had a receipt assigned to a " +
                        $"purchase (transactionId={transactionId} sku={sku} newReceipt={LogObfuscation.ObfuscateIfEnabled(receipt)} " +
                        $"oldReceipt={LogObfuscation.ObfuscateIfEnabled(existingPending.Receipt)})"
                    );
                }

                _entries[key] = new ReceiptEntry.Pending(receipt, restoredQuantity);
            }
        }

        /// <summary>
        /// Marks a line whose native consume succeeded as consumed.
        /// </summary>
        public void MarkConsumed(string transactionId, string sku)
        {
            _entries[(transactionId, sku)] = ReceiptEntry.Consumed.Instance;
        }

        /// <summary>
        /// Marks a line whose native consume ended with an outcome that could not be resolved as refused, so no consume
        /// or delivery this session sends another native consume for it.
        /// </summary>
        public void MarkRefused(string transactionId, string sku)
        {
            _entries[(transactionId, sku)] = ReceiptEntry.Refused.Instance;
        }

        /// <summary>
        /// Consumes a line with its cached token through <paramref name="consumeWithToken"/>, or answers at once: as
        /// unknown when it is not cached, and as a success when it is consumed already.
        /// </summary>
        /// <param name="consumeWithToken">
        /// Sends the native consume with the token it is given and answers it, calling <see cref="MarkConsumed"/> once it
        /// succeeded.
        /// </param>
        /// <param name="onSuccess">Answers a consume that needed no native consume.</param>
        /// <param name="onError">Answers a consume refused before any native consume: unknown, or refused after an unknown outcome.</param>
        public void Consume(
            string transactionId,
            string sku,
            int quantity,
            Action<string> consumeWithToken,
            Action onSuccess,
            Action<string> onError
        ) {
            if (!_entries.TryGetValue((transactionId, sku), out var entry)) {
                XsollaLogger.Debug(Tag, $"[ConsumeProduct] Unrecognized purchase: transactionId={transactionId} sku={sku}");
                onError($"Consumption failed due to the unknown purchase: transactionId={transactionId} sku={sku}");
            } else if (entry is ReceiptEntry.Consumed) {
                // A repeated consume succeeds without a native consume, which would spend another held unit of
                // the SKU. On the Unity IAP paths `XsollaPurchasingStore` absorbs repeated finishes before they get here.
                XsollaLogger.Debug(Tag, $"[ConsumeProduct] Already consumed this session, ignoring duplicate (sku={sku} transactionId={transactionId}).");
                onSuccess();
            } else if (entry is ReceiptEntry.Refused) {
                XsollaLogger.Debug(Tag, $"[ConsumeProduct] Refused, an earlier consume's outcome is unknown (sku={sku} transactionId={transactionId}).");
                onError($"Consumption refused, an earlier consume of this purchase ended with an unknown outcome: transactionId={transactionId} sku={sku}");
            } else if (entry is ReceiptEntry.Pending pending) {
                consumeWithToken(pending.Receipt);
            } else {
                // Unreachable: `ReceiptEntry` is a closed `Pending`|`Consumed`|`Refused` hierarchy. Defensive branch so
                // a future entry kind cannot silently fall through to a no-op that strands the transaction.
                XsollaLogger.Error(Tag, $"[ConsumeProduct] Unexpected receipt entry {entry.GetType().Name} (sku={sku} transactionId={transactionId})");
                onError($"Consumption failed due to an unexpected receipt entry (sku={sku} transactionId={transactionId})");
            }
        }
    }
}
