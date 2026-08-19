using System;
using System.Globalization;
using UnityEngine;

namespace Xsolla.SDK.Utils
{
   
    /// <summary>
    /// Represents locale information including language, country, and currency code.
    /// </summary>
    public sealed class LocaleInfo
    {
        const string Tag = "LocaleInfo";
        
        /// <summary>
        /// The culture info associated with this locale.
        /// </summary>
        public readonly CultureInfo cultureInfo;

        private readonly Lazy<string> countryLazy;

        public static LocaleInfo CreateLocaleInfo(string locale)
        {
            try
            {
                // Accept both the BCP-47/.NET delimiter (en-US) and the Java/Apple one (en_US).
                // CultureInfo requires '-', so normalize '_' first. See SDK-4883.
                var cultureInfo = new CultureInfo(locale?.Replace('_', '-'));
                return new LocaleInfo(cultureInfo);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Xsolla SDK][{Tag}]: Unable to set locale '{locale}'. Exception: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="LocaleInfo"/> class.
        /// </summary>
        /// <param name="cultureInfo">The culture info to use for locale information.</param>
        public LocaleInfo(CultureInfo cultureInfo)
        {
            this.cultureInfo = cultureInfo;

            countryLazy = new Lazy<string>(() => {
                var parts = cultureInfo.Name.Split('-', '-');
                if (parts.Length >= 2)
                    return parts[1].ToUpperInvariant();

                try
                {
                    return new RegionInfo(cultureInfo.Name).TwoLetterISORegionName.ToUpperInvariant();
                }
                catch
                {
                    return string.Empty;
                }
            });
        }

        /// <summary>
        /// Gets the ISO two-letter language code.
        /// </summary>
        public string language => cultureInfo.TwoLetterISOLanguageName;

        /// <summary>
        /// Gets the ISO two-letter country code.
        /// </summary>
        public string country => countryLazy.Value;

        /// <summary>
        /// Gets the ISO currency code (e.g. `EUR`, `USD`, etc.).
        /// </summary>
        public string currencyCode
        {
            get
            {
                try
                {
                    return new RegionInfo(cultureInfo.Name).ISOCurrencySymbol;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        /// <summary>
        /// The locale code in the underscore form (e.g. <c>en_US</c>) expected by the native mobile SDKs.
        /// C#/BCP-47 uses a hyphen (<c>en-US</c>); Apple's <c>Locale</c> accepts both, but Android's
        /// <c>LocaleInfo.parse</c> only matches <c>java.util.Locale.toString()</c> (underscore), so a
        /// hyphenated code is silently dropped there. Pass this to native instead of the raw string. See SDK-4883.
        /// </summary>
        public string nativeCode => cultureInfo.Name.Replace('-', '_');

        /// <summary>
        /// Returns the locale name as a string.
        /// </summary>
        /// <returns>The locale name.</returns>
        public override string ToString() => cultureInfo.Name;

        /// <summary>
        /// Implicitly converts a <see cref="LocaleInfo"/> to its string representation.
        /// </summary>
        /// <param name="localeInfo">The locale info to convert.</param>
        /// <returns>The locale name as a string.</returns>
        public static implicit operator string(LocaleInfo localeInfo) => localeInfo.ToString();

        private static readonly Lazy<LocaleInfo> DefaultLazy = new Lazy<LocaleInfo>(() => new LocaleInfo(CultureInfo.CurrentCulture));

        /// <summary>
        /// Gets the default locale info based on the current culture.
        /// </summary>
        public static LocaleInfo Default => DefaultLazy.Value;
    }
}