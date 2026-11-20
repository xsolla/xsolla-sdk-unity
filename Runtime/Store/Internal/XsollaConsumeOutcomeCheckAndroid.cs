using System;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// Decides what an Android consume whose outcome is unknown did, from the SKU quantities the Java bridge read around
    /// it.
    /// </summary>
    /// <remarks>
    /// The native Android SDK consumes inventory at most once and by SKU: after a <c>5xx</c>, or a connection lost once
    /// the request was written, it cannot tell whether the consume landed, and a retry after one that did consumes
    /// another unit the user owns. The bridge reads the SKU's total quantity before every inventory consume and, a few
    /// seconds after such an answer, again (<c>XsollaStoreClientNativeAndroid.consumeWithOutcomeCheck</c>).
    /// <para/>
    /// Purchases only add units, so when no other consume of the SKU ran meanwhile a drop of exactly the consumed
    /// quantity proves the consume landed, and no change with no purchase meanwhile proves it did not. Anything else is
    /// unresolved and fails closed: the purchase is never consumed again this session, so its units come back with the
    /// next launch's restore (a possible duplicate grant) instead of a unit the user owns being lost. Changes made
    /// outside the app (another device, the server) between the two reads are assumed absent.
    /// <para/>
    /// The bridge checks every non-recoverable error, as native marks the unknown outcome only in its message. An error
    /// whose message lacks that mark is a definite failure (an expired access token, a rejected request) that sent no
    /// consume, so when the inventory does not settle it, it fails as before and may be retried.
    /// </remarks>
    internal static class XsollaConsumeOutcomeCheckAndroid
    {
        /// <summary>
        /// What a consume failure turned out to be.
        /// </summary>
        internal enum Verdict
        {
            /// <summary>
            /// An ordinary consume failure: native did not mark the outcome unknown, and the inventory does not show the
            /// consume landed.
            /// </summary>
            Failed,

            /// <summary>The outcome was unknown, and the inventory shows the consume landed.</summary>
            Consumed,

            /// <summary>The outcome was unknown, and the inventory shows the consume did not land: a retry is safe.</summary>
            NotConsumed,

            /// <summary>The outcome is unknown and stays so: the purchase must not be consumed again this session.</summary>
            Unresolved,
        }

        /// <summary>
        /// What the bridge reports about a consume whose outcome is unknown.
        /// </summary>
        internal sealed class Facts
        {
            /// <summary>The native debug message.</summary>
            [NotNull] public readonly string Message;

            /// <summary>The quantity the consume asked for.</summary>
            public readonly long Quantity;

            /// <summary>The SKU's total quantity read before the consume, <c>null</c> when the read failed.</summary>
            public readonly long? SkuQuantityBefore;

            /// <summary>The SKU's total quantity read after the consume, <c>null</c> when the read failed.</summary>
            public readonly long? SkuQuantityAfter;

            /// <summary>Whether another consume of the SKU was in flight at any time between the two reads.</summary>
            public readonly bool IsOtherConsumeOverlapped;

            /// <summary>Whether a purchase flow started or a purchase result arrived between the two reads.</summary>
            public readonly bool IsPurchaseOverlapped;

            public Facts(
                [CanBeNull] string message,
                long quantity,
                long? skuQuantityBefore,
                long? skuQuantityAfter,
                bool isOtherConsumeOverlapped,
                bool isPurchaseOverlapped
            ) {
                Message = message ?? "";
                Quantity = quantity;
                SkuQuantityBefore = skuQuantityBefore;
                SkuQuantityAfter = skuQuantityAfter;
                IsOtherConsumeOverlapped = isOtherConsumeOverlapped;
                IsPurchaseOverlapped = isPurchaseOverlapped;
            }
        }

        /// <summary>
        /// The opening of the native message for an unknown consume outcome, which the bridge's plain-text fallback
        /// keeps too.
        /// </summary>
        private const string OutcomeUnknownMessage = "The consume outcome is unknown";

        private const string OutcomeKey = "consumeOutcomeUnknown";

        /// <summary>
        /// Classifies the error the bridge reported for a consume.
        /// </summary>
        /// <param name="error">The bridge's error text.</param>
        /// <param name="facts">The facts the bridge reported, <c>null</c> when it reported none.</param>
        internal static Verdict Evaluate([CanBeNull] string error, [CanBeNull] out Facts facts)
        {
            if (TryParseFacts(error, out facts)) {
                return Decide(facts);
            } else if (error != null && error.IndexOf(OutcomeUnknownMessage, StringComparison.Ordinal) >= 0) {
                return Verdict.Unresolved;
            } else {
                return Verdict.Failed;
            }
        }

        /// <summary>
        /// Reads the facts of an unknown consume outcome from the bridge's error JSON.
        /// </summary>
        /// <remarks>
        /// A missing or malformed value reads as unknown (<c>null</c> quantities, overlaps assumed), which
        /// <see cref="Decide"/> resolves to <see cref="Verdict.Unresolved"/>.
        /// </remarks>
        /// <returns>Whether the error is the bridge's JSON for an unknown consume outcome.</returns>
        internal static bool TryParseFacts([CanBeNull] string error, [CanBeNull] out Facts facts)
        {
            facts = null;

            if (string.IsNullOrEmpty(error) || !error.TrimStart().StartsWith("{", StringComparison.Ordinal)) {
                return false;
            }

            JObject json;
            try {
                json = JObject.Parse(error);
            } catch (JsonException) {
                return false;
            }

            if (!(json[OutcomeKey] is JObject outcome)) {
                return false;
            }

            facts = new Facts(
                message: json["message"]?.Type == JTokenType.String ? (string)json["message"] : null,
                quantity: LongOf(outcome["quantity"]) ?? 0,
                skuQuantityBefore: LongOf(outcome["skuQuantityBefore"]),
                skuQuantityAfter: LongOf(outcome["skuQuantityAfter"]),
                isOtherConsumeOverlapped: BoolOf(outcome["otherConsumeOverlapped"]) ?? true,
                isPurchaseOverlapped: BoolOf(outcome["purchaseOverlapped"]) ?? true
            );

            return true;

            long? LongOf(JToken token) => token?.Type == JTokenType.Integer ? (long?)token.Value<long>() : null;

            bool? BoolOf(JToken token) => token?.Type == JTokenType.Boolean ? (bool?)token.Value<bool>() : null;
        }

        /// <summary>
        /// Decides what a consume whose outcome is unknown did.
        /// </summary>
        internal static Verdict Decide([NotNull] Facts facts)
        {
            var isDropReadable = facts.Quantity >= 1 &&
                facts.SkuQuantityBefore >= facts.Quantity &&
                facts.SkuQuantityAfter != null &&
                !facts.IsOtherConsumeOverlapped;
            var drop = isDropReadable ? facts.SkuQuantityBefore - facts.SkuQuantityAfter : null;

            if (drop == facts.Quantity) {
                return Verdict.Consumed;
            } else if (drop == 0 && !facts.IsPurchaseOverlapped) {
                return Verdict.NotConsumed;
            } else if (facts.Message.IndexOf(OutcomeUnknownMessage, StringComparison.Ordinal) >= 0) {
                return Verdict.Unresolved;
            } else {
                return Verdict.Failed;
            }
        }
    }
}
