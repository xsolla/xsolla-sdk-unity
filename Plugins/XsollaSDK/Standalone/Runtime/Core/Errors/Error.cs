using System;
using System.Collections.Generic;
using System.Text;

namespace Xsolla.Core
{
	[Serializable]
	internal class Error
	{
		public string statusCode;
		public string errorCode;
		public string errorMessage;

		public ErrorType ErrorType { get; set; }
		
		public Dictionary<string, string> AdditionalData { get; set; }

		public Error(
			ErrorType errorType = ErrorType.UnknownError, 
			string statusCode = "", string errorCode = "", string errorMessage = "", 
			Dictionary<string, string> data = null
		) {
			ErrorType = errorType;
			this.statusCode = statusCode;
			this.errorCode = errorCode;
			this.errorMessage = errorMessage;
			AdditionalData = data;
		}

		public static Error UnknownError => new Error();

		public override string ToString()
		{
			var builder = new StringBuilder($"Error: {ErrorType}.");

			if (!string.IsNullOrEmpty(statusCode))
				AppendSentence(builder, $"Status code: {statusCode}");

			if (!string.IsNullOrEmpty(errorCode))
				AppendSentence(builder, $"Error code: {errorCode}");

			if (!string.IsNullOrEmpty(errorMessage))
				AppendSentence(builder, $"Message: {errorMessage}");

			if (AdditionalData != null)
			{
				foreach (var item in AdditionalData)
					AppendSentence(builder, $"Data: {item.Key}: {item.Value}");
			}

			return builder.ToString();
		}

		/// <summary>
		/// Appends <paramref name="sentence"/> after a space and closes it with a period, unless it already ends in a
		/// period, an exclamation mark or a question mark, so a text that ends its own sentence is not closed twice.
		/// </summary>
		private static void AppendSentence(StringBuilder builder, string sentence)
		{
			builder.Append(' ').Append(sentence);

			var last = sentence[sentence.Length - 1];
			if (last != '.' && last != '!' && last != '?')
				builder.Append('.');
		}

		public string ToJson()
		{
			return "{" +
				   $"\"code\":\"{ErrorType}\"," +
				   $"\"message\":\"{ToString()}\"," +
				   "}";
		}
	}
}