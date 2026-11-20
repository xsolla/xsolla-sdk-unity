using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System;

namespace Xsolla.Core
{
	internal static class ParseUtils
	{
		private static readonly JsonSerializerSettings serializerSettings;

		static ParseUtils()
		{
			serializerSettings = new JsonSerializerSettings {
				NullValueHandling = NullValueHandling.Ignore
			};
		}

		public static string ToJson<TData>(TData data) where TData : class
		{
			return JsonConvert.SerializeObject(data, serializerSettings);
		}

		public static TData FromJson<TData>(string json) where TData : class
		{
			TData result;

			try
			{
				result = JsonConvert.DeserializeObject<TData>(json, serializerSettings);
			}
			catch (Exception)
			{
				XDebug.LogWarning($"Deserialization failed for {typeof(TData)}");
				result = null;
			}

			return result;
		}

		private static Error ParseError(string json, long? statusCode = null)
		{
			if (json.Contains("statusCode") && json.Contains("errorCode") && json.Contains("errorMessage"))
				return FromJson<Error>(json);

			if (json.Contains("error"))
			{
				if (json.Contains("code") && json.Contains("description"))
					return FromJson<LoginError>(json).ToError();

				if (TryParseShortError(json, statusCode, out var shortError))
					return shortError;
			}

			return null;

			static bool TryParseShortError(string str, long? statusCode, out Error shortError)
			{
				shortError = default;

                // e.g. {"error":"unauthorized"}
                const string needle = "{\"error\":\"";

                var startIndex = str.IndexOf(needle);
                if (startIndex >= 0)
                {
                    var msgStartIndex = startIndex + needle.Length;
                    var msgEndIndex = str.IndexOf('\"', msgStartIndex);

                    if (msgEndIndex >= 0)
                    {
                        var msg = str.Substring(msgStartIndex, msgEndIndex - msgStartIndex);
                        shortError = new Error(
                            statusCode: statusCode != null ? statusCode.ToString() : string.Empty,
                            errorMessage: msg
                        );
						return true;
                    }
                }

				return false;
            }
        }

		public static bool TryParseError(string json, out Error error, long? statusCode = null)
		{
			if (string.IsNullOrEmpty(json))
			{
				error = null;
				return false;
			}

			try
			{
				error = ParseError(json, statusCode);
				return error != null;
			}
			catch (Exception ex)
			{
				error = new Error(ErrorType.InvalidData, errorMessage: ex.Message);
				return true;
			}
		}

		public static bool TryGetValueFromUrl(string url, ParseParameter parameter, out string value)
		{
			var parameterName = parameter.ToString();

			switch (parameter)
			{
				case ParseParameter.error:
				case ParseParameter.error_code:
				case ParseParameter.error_description:
					// Error texts arrive form-encoded: a space as '+', punctuation as a percent escape.
					value = Uri.UnescapeDataString(MatchQueryValue(url, parameterName, "[a-zA-Z0-9._~+%-]").Replace("+", " "));
					break;
				default:
					value = MatchQueryValue(url, parameterName, "[a-zA-Z0-9._+-]");
					XDebug.Log($"Trying to find {parameterName} in URL:{LogRedaction.Redact(url)}");
					break;
			}

			return !string.IsNullOrEmpty(value);
		}

		/// <summary>
		/// The value of the first <paramref name="parameterName"/> in the query of <paramref name="url"/>,
		/// as far as it runs in characters of <paramref name="valueCharacter"/>, or an empty string.
		/// </summary>
		private static string MatchQueryValue(string url, string parameterName, string valueCharacter)
		{
			return new Regex($"[&?]{parameterName}=({valueCharacter}+)").Match(url).Groups[1].Value;
		}
	}
}