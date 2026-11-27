using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#if UNITY_ANDROID || UNITY_EDITOR
using UnityEngine;
#endif

namespace Xsolla.SDK.Store
{
    /// <summary>
    /// The transaction id Unity IAP 4.x granted each purchase of a non-consumable under, kept across launches by the id
    /// the Android SDK made up for the holding the purchase landed in.
    /// <para/>
    /// The Android SDK makes up a holding's id from the project, the user, the SKU and the holding's position, never
    /// from a payment, so the purchase reaches Unity IAP under its invoice id and every restore of the holding under the
    /// made-up one. Unity IAP's transaction log knows only the first, so without this record the first launch after
    /// the purchase grants the item again.
    /// <para/>
    /// The made-up id names the user only when the access token does: under a token that names no user every user of
    /// the device holds the SKU under the same ids, so a record would answer for the holding of another user, whose
    /// restore Unity IAP would then drop as already granted. <see cref="XsollaPurchasingCore"/> records a purchase only
    /// under a token that names a user (see <see cref="NamesUser"/>), and not once its access token was updated after
    /// the purchase, when the inventory it reads may be another user's.
    /// </summary>
    internal sealed class XsollaNonConsumableTransactionIdsAndroid
    {
        private const string KeyPrefix = "Xsolla.SDK.NonConsumableTransactionId.";

        [NotNull] private readonly Func<string, string> _read;

        [NotNull] private readonly Action<string, string> _write;

        private XsollaNonConsumableTransactionIdsAndroid([NotNull] Func<string, string> read, [NotNull] Action<string, string> write)
        {
            _read = read;
            _write = write;
        }

#if UNITY_ANDROID || UNITY_EDITOR
        /// <summary>
        /// Records kept in <see cref="PlayerPrefs"/>, saved at once so a launch that is killed keeps them. The save writes
        /// out every preference of the game not saved yet as well.
        /// <para/>
        /// Cleared with the app data, as Unity IAP's transaction log is. Android Auto Backup may restore the shared
        /// preferences without the log, and then the restore of a recorded holding is granted once under the purchase's
        /// id, as it would be under the made-up one.
        /// </summary>
        public static XsollaNonConsumableTransactionIdsAndroid InPlayerPrefs() =>
            new XsollaNonConsumableTransactionIdsAndroid(
                read: key => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null,
                write: (key, value) =>
                {
                    PlayerPrefs.SetString(key, value);
                    PlayerPrefs.Save();
                }
            );
#endif

        /// <summary>
        /// Records kept in <paramref name="values"/>, which outlives the core as the player prefs do.
        /// </summary>
        public static XsollaNonConsumableTransactionIdsAndroid InDictionary([NotNull] IDictionary<string, string> values) =>
            new XsollaNonConsumableTransactionIdsAndroid(
                read: key => values.TryGetValue(key, out var value) ? value : null,
                write: (key, value) => values[key] = value
            );

        /// <summary>
        /// Records that the holding of <paramref name="sku"/> under the made-up <paramref name="nativeTransactionId"/> was
        /// granted as <paramref name="transactionId"/>, replacing an earlier record of the holding.
        /// </summary>
        public void Record([NotNull] string sku, [NotNull] string nativeTransactionId, [NotNull] string transactionId) =>
            _write(Key(sku, nativeTransactionId), transactionId);

        /// <summary>
        /// The transaction id the holding of <paramref name="sku"/> under the made-up
        /// <paramref name="nativeTransactionId"/> was granted as, or null when no purchase of it was recorded.
        /// </summary>
        [CanBeNull]
        public string Find([NotNull] string sku, [NotNull] string nativeTransactionId) =>
            _read(Key(sku, nativeTransactionId));

        /// <summary>
        /// Whether <paramref name="accessToken"/> names a user: a JWT whose payload carries a sub claim, the one the
        /// Android SDK makes up ids from. Anything that does not read as such names no user.
        /// </summary>
        public static bool NamesUser([CanBeNull] string accessToken)
        {
            var parts = accessToken?.Split('.');
            if (parts == null || parts.Length != 3)
                return false;

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

            JObject claims;
            try
            {
                claims = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            }
            catch (Exception exception) when (exception is FormatException || exception is JsonException)
            {
                return false;
            }

            return claims["sub"] is JValue sub && !string.IsNullOrEmpty(sub.ToString(CultureInfo.InvariantCulture));
        }

        private static string Key(string sku, string nativeTransactionId) =>
            KeyPrefix + nativeTransactionId + ":" + sku;
    }
}
