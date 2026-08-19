using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Common
{
    internal static class XsollaClientHelpers
    {
        private const string Tag = "XsollaClientHelpers";
        
        private static readonly JsonSerializerSettings serializerSettings;

        public static string EmptyJson => "{}";
        
        static XsollaClientHelpers()
        {
            serializerSettings = new JsonSerializerSettings {
                NullValueHandling = NullValueHandling.Ignore
            };
        }
        
        public static T FromJson<T>(string json) where T : class {
            T result;
            try { result = JsonConvert.DeserializeObject<T>(json, serializerSettings); } 
            catch (Exception e) {
                XsollaLogger.Error(Tag, $"Deserialization failed for {typeof(T)} {e}");
                result = null;
            }
            return result;
        }
        
        public static string ToJson<T>(T data) where T : class {
            return JsonConvert.SerializeObject(data, serializerSettings);
        }
        
        public static string ConfigurationToJson(XsollaClientConfiguration configuration)
        {
            // Init payload for the native iOS/Android SDKs. Those parse an underscore locale (en_US),
            // but the config stores whatever the user set (typically hyphen, en-US), which Android's
            // LocaleInfo.parse silently drops. Emit the parsed locale's native (underscore) form so the
            // override actually applies. iOS's Locale accepts both, so this is safe there too. SDK-4883.
            // Parse the normal serialization (unchanged) and override only the locale, so nothing else
            // about the payload can shift.
            var json = JObject.Parse(ToJson(configuration));
            var nativeLocale = configuration.GetCurrentLocale()?.nativeCode;
            if (!string.IsNullOrEmpty(nativeLocale))
                json["locale"] = nativeLocale;
            return json.ToString(Formatting.None);
        }
    }
}
