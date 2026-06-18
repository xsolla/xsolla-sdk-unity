using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Xsolla.Core;

namespace Xsolla.Catalog
{
	internal static class XsollaCatalog
	{
		private const string BaseUrl = "https://store.xsolla.com/api/v2/project";

		// Test seam for the per-chunk page fetch. Production keeps the real GetPaginatedItems, so there
		// is no behavior change; tests swap this to script chunk outcomes (has_more, transient errors,
		// absent SKUs) and exercise the window/drain/retry orchestration without a live backend. Both the
		// SKU window and the full-catalog pager route through it; the internal token-refresh recursion
		// inside GetPaginatedItems deliberately does not (that path retries the real request).
		internal static Action<XsollaSettings, Action<StoreItems>, Action<Error>,
			int, int, string, string, string, bool, SdkType, string[]> PaginatedItemsRequester = GetPaginatedItems;

		// Per-query correlation id for the SKU-fetch trace, so concurrent GetItems calls stay readable in
		// the log. Main-thread only; no synchronization needed.
		private static int _skuFetchSequence;

		/// <summary>
		/// Returns a full list of virtual items.
		/// The list includes items for which display in the store is enabled in the settings. For each virtual item, complete data is returned.
		/// If called after user authentication, the method returns items that match the personalization rules for the current user.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/).</remarks>
		/// <param name="onSuccess">Called after virtual items were successfully received.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="locale">Response language. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="country">Country for which to calculate regional prices and restrictions in a catalog. Two-letter uppercase country code per [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2). Calculations are based on the user's IP address if the country is not specified.  Check the documentation for detailed information about [countries supported by Xsolla](https://developers.xsolla.com/doc/in-game-store/references/supported-countries/).</param>
		/// <param name="additionalFields">The list of additional fields. These fields will be in a response if you send them in a request. Available fields `media_list`, `order`, and `long_description`.</param>
		/// <param name="requestGeoLocale">Should the locale be detected automatically by Xsolla remote services using the geolocation?</param>
		/// <param name="sdkType">SDK type. Used for internal analytics.</param>
		/// <param name="productIds">Optional list of SKUs to request specific items via <c>sku[]</c> query parameters. When <c>null</c> or empty, the full catalog is requested. SKUs are split into batches and requested across multiple calls (a bounded-parallel sliding window); the results are aggregated in request order.</param>
		/// <param name="skipMissingProductsOnFetch">When <c>true</c> (default), SKUs the backend reports as non-existent are skipped with a warning; the fetch fails only when <em>every</em> requested SKU is missing. When <c>false</c>, any missing SKU fails the whole fetch. Applies to the SKU-filtered path only.</param>
		/// <param name="maxSkusPerRequest">SKUs per batch request. Clamped to the backend limit of 50. Applies to the SKU-filtered path only.</param>
		/// <param name="maxParallelRequests">Maximum batch requests in flight at once for this query (1 = strictly sequential). Applies to the SKU-filtered path only.</param>
		/// <param name="retryPolicy">Per-chunk transient-failure retry schedule. When <c>null</c>, <see cref="ProductFetchRetryPolicy.Default"/> is used. Applies to the SKU-filtered path only.</param>
		public static void GetItems(
			XsollaSettings settings,
	        Action<StoreItems> onSuccess,
	        Action<Error> onError,
	        int limit = 50,
	        string locale = null,
	        string country = null,
	        string additionalFields = "long_description",
	        bool requestGeoLocale = false,
	        SdkType sdkType = SdkType.Store,
	        string[] productIds = null,
	        bool skipMissingProductsOnFetch = true,
	        int maxSkusPerRequest = 50,          // backend hard limit; canonical default ProductFetchSettings.DefaultMaxItemsPerRequest
	        int maxParallelRequests = 4,         // canonical default ProductFetchSettings.DefaultMaxParallelRequests (store layer overrides)
	        ProductFetchRetryPolicy retryPolicy = null
	    )
		{
			maxSkusPerRequest = Mathf.Clamp(maxSkusPerRequest, 1, 50);
			maxParallelRequests = Mathf.Max(1, maxParallelRequests);
			retryPolicy ??= ProductFetchRetryPolicy.Default;

			CultureInfo geoLocale = null;

			// No SKU filter: request the full catalog page by page (sequentially, since the
			// total number of pages is not known up front).
			if (productIds == null || productIds.Length == 0)
			{
				var items = new List<StoreItem>();
				var offset = 0;

				processRequest();
				return;

				void processRequest()
				{
					PaginatedItemsRequester(
						settings,
						handleResponse,
						onError,
						limit,
						offset,
						locale,
						country,
						additionalFields,
						requestGeoLocale,
						sdkType,
						null
					);
				}

				void handleResponse(StoreItems response)
				{
					items.AddRange(response.items);

					if (requestGeoLocale && geoLocale == null)
						geoLocale = response.geoLocale;

					if (response.has_more)
					{
						offset += limit;
						processRequest();
						return;
					}

					onSuccess(new StoreItems {
						has_more = false,
						items = items.ToArray(),
						geoLocale = geoLocale
					});
				}
			}

			// SKU filter: split into batches and request them with a bounded-parallel sliding window.
			// Each batch returns at most one page, so the requests are independent. At most
			// maxParallelRequests batches are in flight; the next batch starts as each one settles
			// (on success OR failure — Layer A does not short-circuit). Per-batch outcomes are captured
			// and assembled once every batch settles; query-level short-circuit (Layer B) happens there.
			var fetchId = ++_skuFetchSequence;

			var batches = new List<string[]>();
			for (var i = 0; i < productIds.Length; i += maxSkusPerRequest)
				batches.Add(productIds.Skip(i).Take(maxSkusPerRequest).ToArray());

			if (batches.Count == 0)
			{
				// No real SKUs to request (e.g. an all-empty/whitespace array): never enter the window
				// with nothing to settle, otherwise complete() would never fire and the caller hangs.
				XDebug.LogDebug(settings, $"[Catalog] sku-fetch #{fetchId}: no SKUs to request, returning empty");
				onSuccess(new StoreItems {
					has_more = false,
					items = Array.Empty<StoreItem>(),
					geoLocale = geoLocale
				});
				return;
			}

			XDebug.LogDebug(settings, $"[Catalog] sku-fetch #{fetchId}: {productIds.Length} SKU(s) -> {batches.Count} batch(es) of <={maxSkusPerRequest}, parallel<={maxParallelRequests}, retry<={retryPolicy.MaxAttempts} attempt(s)");

			var batchResults = new List<StoreItem>[batches.Count];
			var batchErrors = new Error[batches.Count];
			for (var b = 0; b < batches.Count; b++)
				batchResults[b] = new List<StoreItem>();

			var settled = new bool[batches.Count];
			var completed = false;
			var remaining = batches.Count;
			var nextBatch = 0;

			for (var i = 0; i < maxParallelRequests && nextBatch < batches.Count; i++)
			{
				var next = nextBatch++;
				requestBatch(next, batches[next]);
			}

			return;

			void requestBatch(int index, string[] skus)
			{
				var inFlight = nextBatch - (batches.Count - remaining);
				XDebug.LogDebug(settings, $"[Catalog] sku-fetch #{fetchId}: -> batch {index} requesting {skus.Length} SKU(s) [{inFlight}/{maxParallelRequests} in flight, {remaining} not settled]");

				GetItemsChunkWithRetry(
					settings,
					skus,
					skus.Length,
					locale,
					country,
					additionalFields,
					requestGeoLocale,
					sdkType,
					retryPolicy,
					fetchId,
					response =>
					{
						batchResults[index].AddRange(response.items);

						if (requestGeoLocale && geoLocale == null)
							geoLocale = response.geoLocale;

						XDebug.LogDebug(settings, $"[Catalog] sku-fetch #{fetchId}: <- batch {index} returned {response.items?.Length ?? 0} item(s){(response.has_more ? " (has_more)" : "")}");

						// Defensive: a bounded sku[] request should never return has_more. If it does,
						// drain only the SKUs of this chunk still missing (a strictly smaller set, since
						// batchResults accumulates across rounds), re-entering without settling.
						if (response.has_more)
						{
							var fetched = new HashSet<string>();
							foreach (var item in batchResults[index])
								fetched.Add(item.sku);
							var missing = batches[index].Where(s => !fetched.Contains(s)).ToArray();

							// Re-request only while making progress and something remains; otherwise the
							// leftover SKUs are genuinely absent and AssembleSkuResults confirms them.
							if (missing.Length > 0 && missing.Length < skus.Length)
							{
								XDebug.LogWarning(settings, $"[Catalog] sku-fetch #{fetchId}: batch {index} returned has_more for a bounded sku[] request; draining {missing.Length} remaining SKU(s)");
								requestBatch(index, missing);
								return;
							}
						}

						settleBatch(index);
					},
					error =>
					{
						// Carry the failure for this batch; do NOT abort the siblings.
						XDebug.LogDebug(settings, $"[Catalog] sku-fetch #{fetchId}: <- batch {index} failed: {error} (window keeps siblings running; query fails at assembly)");
						batchErrors[index] = error;
						settleBatch(index);
					});
			}

			void settleBatch(int index)
			{
				// Ignore a double settle (paging re-entry already settled) or any settle that lands
				// after the query already completed (e.g. a delayed retry resolving post-completion).
				if (completed || settled[index])
					return;
				settled[index] = true;

				remaining--;
				if (remaining == 0)
					complete();
				else if (nextBatch < batches.Count)
				{
					var next = nextBatch++;
					requestBatch(next, batches[next]);
				}
			}

			void complete()
			{
				completed = true;

				var assembly = AssembleSkuResults(productIds, batches, batchResults, batchErrors, skipMissingProductsOnFetch, settings);
				if (assembly.IsSuccess)
				{
					XDebug.LogDebug(settings, $"[Catalog] sku-fetch #{fetchId}: done - assembled {assembly.Items.Length}/{productIds.Length} item(s) across {batches.Count} batch(es)");
					onSuccess(new StoreItems {
						has_more = false,
						items = assembly.Items,
						geoLocale = geoLocale
					});
				}
				else
				{
					XDebug.LogDebug(settings, $"[Catalog] sku-fetch #{fetchId}: done - FAILED across {batches.Count} batch(es): {assembly.Error}");
					onError(assembly.Error);
				}
			}
		}

		internal readonly struct SkuAssemblyResult
		{
			public readonly StoreItem[] Items;
			public readonly Error Error;
			public bool IsSuccess => Error == null;

			private SkuAssemblyResult(StoreItem[] items, Error error)
			{
				Items = items;
				Error = error;
			}

			public static SkuAssemblyResult Success(StoreItem[] items) => new SkuAssemblyResult(items, null);
			public static SkuAssemblyResult Failure(Error error) => new SkuAssemblyResult(null, error);
		}

		// Pure three-way assembly over per-batch outcomes (present / confirmed-absent / failed-chunk),
		// in request order. Layer B short-circuit: the first failed SKU fails the whole query. Then
		// confirmed-absent SKUs are skipped+warned or fail the query per skipMissingProductsOnFetch;
		// an all-absent result is never a valid empty success.
		internal static SkuAssemblyResult AssembleSkuResults(
			string[] productIds,
			List<string[]> batches,
			List<StoreItem>[] batchResults,
			Error[] batchErrors,
			bool skipMissingProductsOnFetch,
			XsollaSettings settings)
		{
			// SKU -> present item (batches hold disjoint SKU sets, so last-wins is harmless).
			var present = new Dictionary<string, StoreItem>(StringComparer.Ordinal);
			foreach (var result in batchResults)
				foreach (var item in result)
					if (item?.sku != null)
						present[item.sku] = item;

			// A failed batch poisons exactly its own requested SKUs.
			var failedSku = new Dictionary<string, Error>(StringComparer.Ordinal);
			for (var b = 0; b < batches.Count; b++)
				if (batchErrors[b] != null)
					foreach (var sku in batches[b])
						failedSku[sku] = batchErrors[b];

			// Layer B short-circuit: the first failure in request order fails the whole query.
			foreach (var sku in productIds)
				if (failedSku.TryGetValue(sku, out var batchError))
					return SkuAssemblyResult.Failure(batchError);

			var assembled = new List<StoreItem>();
			var missing = new List<string>();
			foreach (var sku in productIds)
			{
				if (present.TryGetValue(sku, out var item))
					assembled.Add(item);
				else
					missing.Add(sku); // requested, not failed, not returned => confirmed-absent
			}

			if (missing.Count > 0)
			{
				if (!skipMissingProductsOnFetch)
					return SkuAssemblyResult.Failure(new Error(ErrorType.ProductDoesNotExist,
						errorMessage: $"Requested products do not exist: {string.Join(", ", missing)}"));

				XDebug.LogWarning(settings, $"[Catalog] skipping {missing.Count} non-existent SKU(s): {string.Join(", ", missing)}");
			}

			// All-absent is never a valid empty success, even with skip on.
			if (assembled.Count == 0)
				return SkuAssemblyResult.Failure(new Error(ErrorType.ProductDoesNotExist,
					errorMessage: "No requested products could be fetched"));

			return SkuAssemblyResult.Success(assembled.ToArray());
		}

		// Wraps GetPaginatedItems with per-chunk transient retry driven by the resolved retry policy.
		// Sits below the assembly window, so transients are absorbed before any outcome reaches complete().
		private static void GetItemsChunkWithRetry(
			XsollaSettings settings,
			string[] chunk,
			int limit,
			string locale,
			string country,
			string additionalFields,
			bool requestGeoLocale,
			SdkType sdkType,
			ProductFetchRetryPolicy retryPolicy,
			int fetchId,
			Action<StoreItems> onSuccess,
			Action<Error> onError,
			int attempt = 0)
		{
			PaginatedItemsRequester(
				settings,
				onSuccess,
				error =>
				{
					if (IsTransient(error) && attempt + 1 < retryPolicy.MaxAttempts)
					{
						var delay = retryPolicy.DelaySeconds(attempt);
						XDebug.Log(settings, $"[Catalog] sku-fetch #{fetchId}: transient {error}; retry {attempt + 1}/{retryPolicy.MaxAttempts - 1} in {delay:0.00}s");
						CoroutinesExecutor.Run(RetryAfter(delay, () =>
							GetItemsChunkWithRetry(settings, chunk, limit, locale, country,
								additionalFields, requestGeoLocale, sdkType, retryPolicy, fetchId,
								onSuccess, onError, attempt + 1)));
					}
					else
						onError(error);
				},
				limit,
				0,
				locale,
				country,
				additionalFields,
				requestGeoLocale,
				sdkType,
				chunk);
		}

		private static IEnumerator RetryAfter(float seconds, Action action)
		{
			yield return new WaitForSeconds(seconds);
			action();
		}

		// Transient = transport unreachable + 408 + 429 + 5xx. Everything else is absolute.
		internal static bool IsTransient(Error error)
		{
			if (error.ErrorType == ErrorType.NetworkError)
				return true;

			switch (error.statusCode)
			{
				case "408":
				case "429":
				case "500":
				case "502":
				case "503":
				case "504":
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// Returns a list of virtual items according to pagination settings.
		/// The list includes items for which display in the store is enabled in the settings. For each virtual item, complete data is returned.
		/// If called after user authentication, the method returns items that match the personalization rules for the current user.
		/// <b>Attention:</b> The number of items returned in a single response is limited. <b>The default and maximum value is 50 items per response</b>. To get more data page by page, use <code>limit</code> and <code>offset</code> fields.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/).</remarks>
		/// <param name="onSuccess">Called after virtual items were successfully received.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="limit">Limit for the number of elements on the page. The maximum number of elements on a page is 50.</param>
		/// <param name="offset">Number of the element from which the list is generated (the count starts from 0).</param>
		/// <param name="locale">Response language. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="country">Country for which to calculate regional prices and restrictions in a catalog. Two-letter uppercase country code per [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2). Calculations are based on the user's IP address if the country is not specified.  Check the documentation for detailed information about [countries supported by Xsolla](https://developers.xsolla.com/doc/in-game-store/references/supported-countries/).</param>
		/// <param name="additionalFields">The list of additional fields. These fields will be in a response if you send them in a request. Available fields `media_list`, `order`, and `long_description`.</param>
		/// <param name="requestGeoLocale">See `requestGeoLocale` in <see cref="GetItems"/>.</param>
		/// <param name="sdkType">SDK type. Used for internal analytics.</param>
		/// <param name="productIds">Optional list of SKUs to request specific items via <c>sku[]</c> query parameters. When <c>null</c> or empty, the full catalog page is requested. This is a single request, so the caller is responsible for keeping the list within the API limit of 50 SKUs.</param>
		public static void GetPaginatedItems(
			XsollaSettings settings, 
	        Action<StoreItems> onSuccess,
	        Action<Error> onError,
	        int limit, int offset,
	        string locale = null,
	        string country = null,
	        string additionalFields = "long_description",
	        bool requestGeoLocale = false,
	        SdkType sdkType = SdkType.Store,
	        string[] productIds = null
	    )
		{
			var url = new UrlBuilder($"{BaseUrl}/{settings.StoreProjectId}/items/virtual_items")
				.AddLimit(limit)
				.AddOffset(offset)
				.AddLocale(locale)
				.AddCountry(country)
				.AddAdditionalFields(additionalFields)
				.AddParam("with_geo", requestGeoLocale.ToString())
				.AddArray("sku", productIds)
				.Build();

			WebRequestHelper.Instance.GetRequest<StoreItems>(
				sdkType,
				url,
				requestHeader: WebRequestHeader.AuthHeader(settings),
				onComplete: (storeItems, responseHeaders) =>
		        {
		            XDebug.Log(settings,$"[Catalog] requested geo detection: {requestGeoLocale}");

		            if (requestGeoLocale &&
		                responseHeaders.Count > 0 &&
		                responseHeaders.TryGetValue("X-User-Locale-Code", out var languageCode) &&
		                !string.IsNullOrEmpty(languageCode) &&
		                responseHeaders.TryGetValue("X-User-Country-Code", out var countryCode) &&
		                !string.IsNullOrEmpty(countryCode))
		            {
		                try
		                {
		                    var code = $"{languageCode.ToLowerInvariant()}-{countryCode.ToUpperInvariant()}";

		                    var culture = new CultureInfo(code);

		                    XDebug.Log(settings, $"[Catalog] geo detected culture: {code}");

		                    var updatedStoreItems = new StoreItems
		                    {
		                        items = storeItems.items,
		                        has_more = storeItems.has_more,
		                        geoLocale = culture
		                    };

		                    onSuccess(updatedStoreItems);
		                }
		                catch(Exception ex) {
		                    XDebug.LogError(settings, ex.ToString());
		                }
		            }
		            else
		            {
		                onSuccess(storeItems);
		            }
		        },
				onError: error => TokenAutoRefresher.Check(settings, error, onError, () =>
		            GetPaginatedItems(settings, onSuccess, onError, limit, offset, locale, country, additionalFields, requestGeoLocale, sdkType, productIds)
		        ),
				errorsToCheck: ErrorGroup.ItemsListErrors);
		}

		/// <summary>
		/// Returns a full list of virtual items. The list includes items for which display in the store is enabled in the settings. For each virtual item, the SKU, name, description, and data about the groups it belongs to are returned.
		/// If used after user authentication, the method returns items that match the personalization rules for the current user.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/).</remarks>
		/// <param name="onSuccess">Called after server response.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="locale">Response language. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		public static void GetCatalogSimplified(XsollaSettings settings, Action<StoreShortItems> onSuccess, Action<Error> onError, string locale = null)
		{
			var url = new UrlBuilder($"{BaseUrl}/{settings.StoreProjectId}/items/virtual_items/all")
				.AddLocale(locale)
				.Build();

			WebRequestHelper.Instance.GetRequest(
				SdkType.Store,
				url,
				WebRequestHeader.AuthHeader(settings),
				onSuccess,
				error => TokenAutoRefresher.Check(settings, error, onError, () => GetCatalogSimplified(settings, onSuccess, onError, locale)),
				ErrorGroup.ItemsListErrors);
		}

		/// <summary>
		/// Returns a list of items for the specified group. The list includes items for which display in the store is enabled in the settings. In the settings of the group, the display in the store must be enabled.
		/// If called after user authentication, the method returns items that match the personalization rules for the current user.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/).</remarks>
		/// <param name="groupExternalId">Group external ID.</param>
		/// <param name="onSuccess">Called after server response.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="locale">Response language. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="country">Country for which to calculate regional prices and restrictions in a catalog. Two-letter uppercase country code per [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2). Calculations are based on the user's IP address if the country is not specified.  Check the documentation for detailed information about [countries supported by Xsolla](https://developers.xsolla.com/doc/in-game-store/references/supported-countries/).</param>
		/// <param name="additionalFields">The list of additional fields. This fields will be in a response if you send its in a request. Available fields `media_list`, `order`, `long_description`.</param>
		public static void GetGroupItems(XsollaSettings settings, string groupExternalId, Action<StoreItems> onSuccess, Action<Error> onError, string locale = null, string country = null, string additionalFields = null)
		{
			var items = new List<StoreItem>();
			const int limit = 50;
			var offset = 0;

			processRequest();
			return;

			void processRequest()
			{
				GetPaginatedGroupItems(
					settings,
					groupExternalId,
					handleResponse,
					onError,
					limit,
					offset,
					locale,
					country,
					additionalFields);
			}

			void handleResponse(StoreItems response)
			{
				items.AddRange(response.items);

				if (!response.has_more)
				{
					onSuccess(new StoreItems {
						has_more = false,
						items = items.ToArray()
					});
				}
				else
				{
					offset += limit;
					processRequest();
				}
			}
		}

		/// <summary>
		/// Returns a list of items for the specified group according to pagination settings. The list includes items for which display in the store is enabled in the settings. In the settings of the group, the display in the store must be enabled.
		/// If called after user authentication, the method returns items that match the personalization rules for the current user.
		/// <b>Attention:</b> The number of items returned in a single response is limited. <b>The default and maximum value is 50 items per response</b>. To get more data page by page, use <code>limit</code> and <code>offset</code> fields.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/).</remarks>
		/// <param name="groupExternalId">Group external ID.</param>
		/// <param name="onSuccess">Called after server response.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="limit">Limit for the number of elements on the page. The maximum number of elements on a page is 50.</param>
		/// <param name="offset">Number of the element from which the list is generated (the count starts from 0).</param>
		/// <param name="locale">Response language. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="country">Country for which to calculate regional prices and restrictions in a catalog. Two-letter uppercase country code per [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2). Calculations are based on the user's IP address if the country is not specified.  Check the documentation for detailed information about [countries supported by Xsolla](https://developers.xsolla.com/doc/in-game-store/references/supported-countries/).</param>
		/// <param name="additionalFields">The list of additional fields. This fields will be in a response if you send its in a request. Available fields `media_list`, `order`, `long_description`.</param>
		public static void GetPaginatedGroupItems(XsollaSettings settings, string groupExternalId, Action<StoreItems> onSuccess, Action<Error> onError, int limit, int offset, string locale = null, string country = null, string additionalFields = null)
		{
			var url = new UrlBuilder($"{BaseUrl}/{settings.StoreProjectId}/items/virtual_items/group/{groupExternalId}")
				.AddLimit(limit)
				.AddOffset(offset)
				.AddLocale(locale)
				.AddCountry(country)
				.AddAdditionalFields(additionalFields)
				.Build();

			WebRequestHelper.Instance.GetRequest(
				SdkType.Store,
				url,
				WebRequestHeader.AuthHeader(settings),
				onSuccess,
				error => TokenAutoRefresher.Check(settings, error, onError, () => GetPaginatedGroupItems(settings, groupExternalId, onSuccess, onError, limit, offset, locale, country, additionalFields)),
				ErrorGroup.ItemsListErrors);
		}

		/// <summary>
		/// Returns a full list of virtual item groups. The list includes groups for which display in the store is enabled in the settings.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/).</remarks>
		/// <param name="onSuccess">Called after virtual item groups were successfully received.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="locale">Defines localization of the item text fields.[Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="promoCode">Promo code. Unique case-sensitive code. Contains letters and numbers.</param>
		public static void GetItemGroups(XsollaSettings settings, Action<Groups> onSuccess, Action<Error> onError, string locale = null, string promoCode = null)
		{
			var url = new UrlBuilder($"{BaseUrl}/{settings.StoreProjectId}/items/groups")
				.AddLocale(locale)
				.AddParam("promo_code", promoCode)
				.Build();

			WebRequestHelper.Instance.GetRequest(
				SdkType.Store,
				url,
				onSuccess,
				error => TokenAutoRefresher.Check(settings, error, onError, () => GetItemGroups(settings, onSuccess, onError, locale)));
		}

		/// <summary>
		/// Returns a full list of virtual currencies. The list includes currencies for which display in the store is enabled in settings.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/).</remarks>
		/// <param name="onSuccess">Called after virtual currencies were successfully received.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="locale">Response language. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="country">Country for which to calculate regional prices and restrictions in a catalog. Two-letter uppercase country code per [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2). Calculations are based on the user's IP address if the country is not specified.  Check the documentation for detailed information about [countries supported by Xsolla](https://developers.xsolla.com/doc/in-game-store/references/supported-countries/).</param>
		/// <param name="additionalFields">The list of additional fields. These fields will be in a response if you send them in a request. Available fields `media_list`, `order`, and `long_description`.</param>
		public static void GetVirtualCurrencyList(XsollaSettings settings, Action<VirtualCurrencyItems> onSuccess, Action<Error> onError, string locale = null, string country = null, string additionalFields = null)
		{
			var items = new List<VirtualCurrencyItem>();
			const int limit = 50;
			var offset = 0;

			processRequest();
			return;

			void processRequest()
			{
				GetPaginatedVirtualCurrencyList(
					settings,
					handleResponse,
					onError,
					limit,
					offset,
					locale,
					country,
					additionalFields);
			}

			void handleResponse(VirtualCurrencyItems response)
			{
				items.AddRange(response.items);

				if (!response.has_more)
				{
					onSuccess(new VirtualCurrencyItems {
						has_more = false,
						items = items.ToArray()
					});
				}
				else
				{
					offset += limit;
					processRequest();
				}
			}
		}

		/// <summary>
		/// Returns a list of virtual currencies according to pagination settings. The list includes currencies for which display in the store is enabled in settings.
		/// <b>Attention:</b> The number of currencies returned in a single response is limited. <b>The default and maximum value is 50 currencies per response</b>. To get more data page by page, use <code>limit</code> and <code>offset</code> fields.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/).</remarks>
		/// <param name="onSuccess">Called after virtual currencies were successfully received.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="limit">Limit for the number of elements on the page. The maximum number of elements on a page is 50.</param>
		/// <param name="offset">Number of the element from which the list is generated (the count starts from 0).</param>
		/// <param name="locale">Response language. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="country">Country for which to calculate regional prices and restrictions in a catalog. Two-letter uppercase country code per [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2). Calculations are based on the user's IP address if the country is not specified.  Check the documentation for detailed information about [countries supported by Xsolla](https://developers.xsolla.com/doc/in-game-store/references/supported-countries/).</param>
		/// <param name="additionalFields">The list of additional fields. These fields will be in a response if you send them in a request. Available fields `media_list`, `order`, and `long_description`.</param>
		public static void GetPaginatedVirtualCurrencyList(XsollaSettings settings, Action<VirtualCurrencyItems> onSuccess, Action<Error> onError, int limit, int offset, string locale = null, string country = null, string additionalFields = null)
		{
			var url = new UrlBuilder($"{BaseUrl}/{settings.StoreProjectId}/items/virtual_currency")
				.AddLimit(limit)
				.AddOffset(offset)
				.AddLocale(locale)
				.AddCountry(country)
				.AddAdditionalFields(additionalFields)
				.Build();

			WebRequestHelper.Instance.GetRequest(
				SdkType.Store,
				url,
				onSuccess,
				error => TokenAutoRefresher.Check(settings, error, onError, () => GetPaginatedVirtualCurrencyList(settings, onSuccess, onError, limit, offset, locale, country, additionalFields)),
				ErrorGroup.ItemsListErrors);
		}

		/// <summary>
		/// Returns a list of full virtual currency packages.
		/// The list includes packages for which display in the store is enabled in the settings.
		/// If called after user authentication, the method returns packages that match the personalization rules for the current user.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/).</remarks>
		/// <param name="onSuccess">Called after virtual currency packages were successfully received.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="locale">Response language. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="country">Country for which to calculate regional prices and restrictions in a catalog. Two-letter uppercase country code per [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2). Calculations are based on the user's IP address if the country is not specified.  Check the documentation for detailed information about [countries supported by Xsolla](https://developers.xsolla.com/doc/in-game-store/references/supported-countries/).</param>
		/// <param name="additionalFields">The list of additional fields. These fields will be in a response if you send them in a request. Available fields `media_list`, `order`, and `long_description`.</param>
		/// <param name="sdkType">SDK type. Used for internal analytics.</param>
		public static void GetVirtualCurrencyPackagesList(XsollaSettings settings, Action<VirtualCurrencyPackages> onSuccess, Action<Error> onError, string locale = null, string country = null, string additionalFields = null, SdkType sdkType = SdkType.Store)
		{
			var items = new List<VirtualCurrencyPackage>();
			const int limit = 50;
			var offset = 0;

			processRequest();
			return;

			void processRequest()
			{
				GetPaginatedVirtualCurrencyPackagesList(
					settings,
					handleResponse,
					onError,
					limit,
					offset,
					locale,
					country,
					additionalFields,
					sdkType);
			}

			void handleResponse(VirtualCurrencyPackages response)
			{
				items.AddRange(response.items);

				if (!response.has_more)
				{
					onSuccess(new VirtualCurrencyPackages {
						has_more = false,
						items = items.ToArray()
					});
				}
				else
				{
					offset += limit;
					processRequest();
				}
			}
		}

		/// <summary>
		/// Returns a list of virtual currency packages according to pagination settings.
		/// The list includes packages for which display in the store is enabled in the settings.
		/// If called after user authentication, the method returns packages that match the personalization rules for the current user.
		/// <b>Attention:</b> The number of packages returned in a single response is limited. <b>The default and maximum value is 50 packages per response</b>. To get more data page by page, use <code>limit</code> and <code>offset</code> fields.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/).</remarks>
		/// <param name="onSuccess">Called after virtual currency packages were successfully received.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="limit">Limit for the number of elements on the page. The maximum number of elements on a page is 50.</param>
		/// <param name="offset">Number of the element from which the list is generated (the count starts from 0).</param>
		/// <param name="locale">Response language. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="country">Country for which to calculate regional prices and restrictions in a catalog. Two-letter uppercase country code per [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2). Calculations are based on the user's IP address if the country is not specified.  Check the documentation for detailed information about [countries supported by Xsolla](https://developers.xsolla.com/doc/in-game-store/references/supported-countries/).</param>
		/// <param name="additionalFields">The list of additional fields. These fields will be in a response if you send them in a request. Available fields `media_list`, `order`, and `long_description`.</param>
		/// <param name="sdkType">SDK type. Used for internal analytics.</param>
		public static void GetPaginatedVirtualCurrencyPackagesList(XsollaSettings settings, Action<VirtualCurrencyPackages> onSuccess, Action<Error> onError, int limit, int offset, string locale = null, string country = null, string additionalFields = null, SdkType sdkType = SdkType.Store)
		{
			var url = new UrlBuilder($"{BaseUrl}/{settings.StoreProjectId}/items/virtual_currency/package")
				.AddLimit(limit)
				.AddOffset(offset)
				.AddLocale(locale)
				.AddCountry(country)
				.AddAdditionalFields(additionalFields)
				.Build();

			WebRequestHelper.Instance.GetRequest(
				sdkType,
				url,
				WebRequestHeader.AuthHeader(settings),
				onSuccess,
				error => TokenAutoRefresher.Check(settings, error, onError, () => GetPaginatedVirtualCurrencyPackagesList(settings, onSuccess, onError, limit, offset, locale, country, additionalFields)),
				ErrorGroup.ItemsListErrors);
		}

		/// <summary>
		/// Returns a full  list of bundles.
		/// The list includes bundles for which display in the store is enabled in the settings.
		/// If called after user authentication, the method returns items that match the personalization rules for the current user.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/#unreal_engine_sdk_how_to_bundles).</remarks>
		/// <param name="onSuccess">Called after bundles are successfully received.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="locale">Defines localization of the item text fields. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="country">Country for which to calculate regional prices and restrictions in a catalog. Two-letter uppercase country code per [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2). Calculations are based on the user's IP address if the country is not specified.  Check the documentation for detailed information about [countries supported by Xsolla](https://developers.xsolla.com/doc/in-game-store/references/supported-countries/).</param>
		/// <param name="additionalFields">The list of additional fields. This fields will be in a response if you send its in a request. Available fields `media_list`, `order`, `long_description`.</param>
		public static void GetBundles(XsollaSettings settings, Action<BundleItems> onSuccess, Action<Error> onError, string locale = null, string country = null, string additionalFields = null)
		{
			var items = new List<BundleItem>();
			const int limit = 50;
			var offset = 0;

			processRequest();
			return;

			void processRequest()
			{
				GetPaginatedBundles(
					settings,
					handleResponse,
					onError,
					limit,
					offset,
					locale,
					country,
					additionalFields);
			}

			void handleResponse(BundleItems response)
			{
				items.AddRange(response.items);

				if (!response.has_more)
				{
					onSuccess(new BundleItems {
						has_more = false,
						items = items.ToArray()
					});
				}
				else
				{
					offset += limit;
					processRequest();
				}
			}
		}

		/// <summary>
		/// Returns a list of bundles according to pagination settings.
		/// The list includes bundles for which display in the store is enabled in the settings.
		/// If called after user authentication, the method returns items that match the personalization rules for the current user.
		/// <b>Attention:</b> The number of bundles returned in a single response is limited. <b>The default and maximum value is 50 bundles per response</b>. To get more data page by page, use <code>limit</code> and <code>offset</code> fields. 
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/#unreal_engine_sdk_how_to_bundles).</remarks>
		/// <param name="onSuccess">Called after bundles are successfully received.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="limit">Limit for the number of elements on the page. The maximum number of elements on a page is 50.</param>
		/// <param name="offset">Number of the element from which the list is generated (the count starts from 0).</param>
		/// <param name="locale">Defines localization of the item text fields. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="country">Country for which to calculate regional prices and restrictions in a catalog. Two-letter uppercase country code per [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2). Calculations are based on the user's IP address if the country is not specified.  Check the documentation for detailed information about [countries supported by Xsolla](https://developers.xsolla.com/doc/in-game-store/references/supported-countries/).</param>
		/// <param name="additionalFields">The list of additional fields. This fields will be in a response if you send its in a request. Available fields `media_list`, `order`, `long_description`.</param>
		public static void GetPaginatedBundles(XsollaSettings settings, Action<BundleItems> onSuccess, Action<Error> onError, int limit, int offset, string locale = null, string country = null, string additionalFields = null)
		{
			var url = new UrlBuilder($"{BaseUrl}/{settings.StoreProjectId}/items/bundle")
				.AddLimit(limit)
				.AddOffset(offset)
				.AddLocale(locale)
				.AddCountry(country)
				.AddAdditionalFields(additionalFields)
				.Build();

			WebRequestHelper.Instance.GetRequest(
				SdkType.Store,
				url,
				WebRequestHeader.AuthHeader(settings),
				onSuccess,
				error => TokenAutoRefresher.Check(settings, error, onError, () => GetPaginatedBundles(settings, onSuccess, onError, limit, offset, locale, country, additionalFields)),
				ErrorGroup.ItemsListErrors);
		}

		/// <summary>
		/// Returns information about the contents of the specified bundle. In the bundle settings, display in the store must be enabled.
		/// If used after user authentication, the method returns items that match the personalization rules for the current user.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/#unreal_engine_sdk_how_to_bundles).</remarks>
		/// <param name="sku">Bundle SKU.</param>
		/// <param name="onSuccess">Called after the cart is successfully filled.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="locale">Defines localization of the item text fields. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="country">Country for which to calculate regional prices and restrictions in a catalog. Two-letter uppercase country code per [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2). Calculations are based on the user's IP address if the country is not specified.  Check the documentation for detailed information about [countries supported by Xsolla](https://developers.xsolla.com/doc/in-game-store/references/supported-countries/).</param>
		public static void GetBundle(XsollaSettings settings, string sku, Action<BundleItem> onSuccess, Action<Error> onError, string locale = null, string country = null)
		{
			var url = new UrlBuilder($"{BaseUrl}/{settings.StoreProjectId}/items/bundle/sku/{sku}")
				.AddLocale(locale)
				.AddCountry(country)
				.Build();

			WebRequestHelper.Instance.GetRequest(
				SdkType.Store,
				url,
				WebRequestHeader.AuthHeader(settings),
				onSuccess,
				error => TokenAutoRefresher.Check(settings, error, onError, () => GetBundle(settings, sku, onSuccess, onError, locale, country)),
				ErrorGroup.ItemsListErrors);
		}

		/// <summary>
		/// Redeems the coupon code and delivers a reward to the user in one of the following ways:
		/// - to their inventory (virtual items, virtual currency packages, or bundles)
		/// - via email (game keys)
		/// - to the entitlement system (game keys)
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/promo/coupons).</remarks>
		/// <param name="couponCode">Unique case sensitive code. Contains letters and numbers.</param>
		/// <param name="onSuccess">Called after successful coupon redemption.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		public static void RedeemCouponCode(XsollaSettings settings, string couponCode, Action<CouponRedeemedItems> onSuccess, Action<Error> onError)
		{
			var url = $"{BaseUrl}/{settings.StoreProjectId}/coupon/redeem";

			var headers = new List<WebRequestHeader> {
				WebRequestHeader.AuthHeader(settings),
				WebRequestHeader.JsonContentTypeHeader()
			};

			var requestData = new CouponCodeRequest {
				coupon_code = couponCode
			};

			WebRequestHelper.Instance.PostRequest(
				SdkType.Store,
				url,
				requestData,
				headers,
				onSuccess,
				error => TokenAutoRefresher.Check(settings, error, onError, () => RedeemCouponCode(settings, couponCode, onSuccess, onError)),
				ErrorGroup.CouponErrors);
		}

		/// <summary>
		/// Returns a list of items that can be credited to the user when the coupon is redeemed. Can be used to let users choose one of many items as a bonus. The usual case is choosing a DRM if the coupon contains a game as a bonus.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/promo/coupons).</remarks>
		/// <param name="couponCode">Unique case sensitive code. Contains letters and numbers.</param>
		/// <param name="onSuccess">Called after receiving coupon rewards successfully.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		public static void GetCouponRewards(XsollaSettings settings, string couponCode, Action<CouponReward> onSuccess, Action<Error> onError)
		{
			var url = $"{BaseUrl}/{settings.StoreProjectId}/coupon/code/{couponCode}/rewards";

			var headers = new List<WebRequestHeader> {
				WebRequestHeader.AuthHeader(settings),
				WebRequestHeader.JsonContentTypeHeader()
			};

			WebRequestHelper.Instance.GetRequest(
				SdkType.Store,
				url,
				headers,
				onSuccess,
				error => TokenAutoRefresher.Check(settings, error, onError, () => GetCouponRewards(settings, couponCode, onSuccess, onError)),
				ErrorGroup.CouponErrors);
		}

		/// <summary>
		/// Creates an order with a specified item. The created order will get a `new` order status.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/item-purchase/one-click-purchase/).</remarks>
		/// <param name="itemSku">Item SKU to purchase.</param>
		/// <param name="onSuccess">Called after server response.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="purchaseParams">Purchase parameters such as <c>country</c>, <c>locale</c>, and <c>currency</c>.</param>
		/// <param name="customHeaders">Custom web request headers</param>
		/// <param name="sdkType">SDK type. Used for internal analytics.</param>
		public static void CreateOrder(XsollaSettings settings, string itemSku, Action<OrderData> onSuccess, Action<Error> onError, PurchaseParams purchaseParams = null, Dictionary<string, string> customHeaders = null, SdkType sdkType = SdkType.Store)
		{
			if (CreateOrderCooldown.IsActive)
				return;

			CreateOrderCooldown.Start();

			var url = $"{BaseUrl}/{settings.StoreProjectId}/payment/item/{itemSku}";
			var requestData = PurchaseParamsGenerator.GeneratePurchaseParamsRequest(settings, purchaseParams);
			var headers = PurchaseParamsGenerator.GeneratePaymentHeaders(settings, customHeaders);

			WebRequestHelper.Instance.PostRequest<OrderData, PurchaseParamsRequest>(
				sdkType,
				url,
				requestData,
				headers,
				orderData => {
					CreateOrderCooldown.Cancel();
					onSuccess?.Invoke(orderData);
				},
				error => {
					CreateOrderCooldown.Cancel();
					TokenAutoRefresher.Check(settings, error, onError, () => CreateOrder(settings, itemSku, onSuccess, onError, purchaseParams, customHeaders));
				},
				ErrorGroup.BuyItemErrors);
		}

		/// <summary>
		/// Creates an order with a specified item, returns unique identifier of the created order and the Pay Station token for the purchase of the specified product by virtual currency. The created order will get a `new` order status.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/item-purchase/purchase-for-vc/).</remarks>
		/// <param name="itemSku">Item SKU to purchase.</param>
		/// <param name="priceSku">Virtual currency SKU.</param>
		/// <param name="onSuccess">Called after server response.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="purchaseParams">Purchase parameters such as <c>country</c>, <c>locale</c>, <c>currency</c>, and <c>quantity</c>.</param>
		/// <param name="platform">Publishing platform the user plays on.<br/>
		///     Can be `xsolla` (default), `playstation_network`, `xbox_live`, `pc_standalone`, `nintendo_shop`, `google_play`, `app_store_ios`, `android_standalone`, `ios_standalone`, `android_other`, `ios_other`, or `pc_other`.</param>
		/// <param name="customHeaders">Custom HTTP request headers.</param>
		public static void CreateOrderByVirtualCurrency(XsollaSettings settings, string itemSku, string priceSku, Action<OrderId> onSuccess, Action<Error> onError, PurchaseParams purchaseParams = null, string platform = null, Dictionary<string, string> customHeaders = null)
		{
			var url = new UrlBuilder($"{BaseUrl}/{settings.StoreProjectId}/payment/item/{itemSku}/virtual/{priceSku}")
				.AddPlatform(platform)
				.Build();

			var requestData = new PurchaseParamsRequest {
				sandbox = settings.IsSandbox,
				custom_parameters = purchaseParams?.custom_parameters
			};

			var headers = PurchaseParamsGenerator.GeneratePaymentHeaders(settings, customHeaders);

			WebRequestHelper.Instance.PostRequest(
				SdkType.Store,
				url,
				requestData,
				headers,
				onSuccess,
				error => TokenAutoRefresher.Check(settings, error, onError, () => CreateOrderByVirtualCurrency(settings, itemSku, priceSku, onSuccess, onError, purchaseParams, platform, customHeaders)),
				ErrorGroup.BuyItemErrors);
		}

		/// <summary>
		/// Create order with specified free item. The created order will get a `done` order status.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/promo/free-items/).</remarks>
		/// <param name="itemSku">Desired free item SKU.</param>
		/// <param name="onSuccess">Called after the order was successfully created.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="purchaseParams">Purchase parameters such as <c>country</c>, <c>locale</c>, and <c>currency</c>.</param>
		/// <param name="customHeaders">Custom web request headers</param>
		public static void CreateOrderWithFreeItem(XsollaSettings settings, string itemSku, Action<OrderId> onSuccess, Action<Error> onError, PurchaseParams purchaseParams = null, Dictionary<string, string> customHeaders = null)
		{
			var url = $"{BaseUrl}/{settings.StoreProjectId}/free/item/{itemSku}";
			var requestData = PurchaseParamsGenerator.GeneratePurchaseParamsRequest(settings, purchaseParams);
			var headers = PurchaseParamsGenerator.GeneratePaymentHeaders(settings, customHeaders);

			WebRequestHelper.Instance.PostRequest<OrderId, PurchaseParamsRequest>(
				SdkType.Store,
				url,
				requestData,
				headers,
				purchaseData => onSuccess?.Invoke(purchaseData),
				error => TokenAutoRefresher.Check(settings, error, onError, () => CreateOrderWithFreeItem(settings, itemSku, onSuccess, onError, purchaseParams, customHeaders)),
				ErrorGroup.BuyItemErrors);
		}

		/// <summary>
		/// Launches purchase process for a specified item. This method encapsulates methods for creating an order, opening a payment UI, and tracking the order status.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/item-purchase/one-click-purchase/).</remarks>
		/// <param name="itemSku">Desired free item SKU.</param>
		/// <param name="onSuccess">Called after the order transitions to the 'done' status.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="onOrderCreated">Called after the order is created.</param>
		/// <param name="onBrowseClosed">Called after the browser is closed. The event is tracked only when the payment UI is opened in the built-in browser. External browser events can't be tracked.</param>
		/// <param name="purchaseParams">Purchase and payment UI parameters, such as <c>locale</c>, <c>currency</c>, etc.</param>
		/// <param name="customHeaders">Custom web request headers</param>
		/// <param name="platformSpecificAppearance">Additional settings of payment UI appearance for different platforms.</param>
		/// <param name="sdkType">SDK type. Used for internal analytics.</param>
		public static void Purchase(XsollaSettings settings, string itemSku, Action<OrderStatus> onSuccess, Action<Error> onError, Action<OrderData> onOrderCreated = null, Action<BrowserCloseInfo> onBrowseClosed = null, PurchaseParams purchaseParams = null, Dictionary<string, string> customHeaders = null, PlatformSpecificAppearance platformSpecificAppearance = null, SdkType sdkType = SdkType.Store)
		{
			CreateOrder(
				settings,
				itemSku,
				orderData => {
					onOrderCreated?.Invoke(orderData);

					XsollaWebBrowser.OpenPurchaseUI(
						settings,
						orderData.token,
						false,
						onBrowseClosed,
						platformSpecificAppearance,
						sdkType);

					OrderTrackingService.AddOrderForTracking(
						settings,
						orderData.order_id,
						true,
						status => {
							EventManagerProvider.Handler?.BroadcastEvent(EventTypes.PaystationCompleted);
							
							XsollaWebBrowser.Close();
							if (settings.EventApiEnabled) {
								onSuccess?.Invoke(status);
							} else {
								OrderStatusService.GetOrderInfo(settings, accessToken: orderData.token, sdkType,
									onSuccess: orderInfo => {
										if (orderInfo.TryAsDone(out var done)) {
											status.transaction_id = done.invoiceId.ToString();
											onSuccess(status);
										} 
										else 
										{
											onError.Invoke(new Error(errorMessage: $"Wrong post payment order status, expected 'done' (order_id={orderData.order_id})"));
										}
									},
									onFailure: err => {
										onError.Invoke(new Error(errorMessage: $"Post payment order status query failed (order_id={orderData.order_id}):\n{err}"));
									}
								);
							}
						},
						onError: error =>
						{
							if (error.ErrorType == ErrorType.UserCancelled)
								EventManagerProvider.Handler?.BroadcastEvent(EventTypes.PaystationCancelled);
							
							onError?.Invoke(error);
						},
						sdkType
					);
				},
				onError,
				purchaseParams,
				customHeaders,
				sdkType);
		}

		/// <summary>
		/// Launch purchase process for a specified item by virtual currency. This method encapsulates methods for creating an order and tracking the order status.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/item-purchase/purchase-for-vc/).</remarks>
		/// <param name="itemSku">Desired free item SKU.</param>
		/// <param name="priceSku">Virtual currency SKU.</param>
		/// <param name="onSuccess">Called after the order transitions to the 'done' status.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="onOrderCreated">Called after the order is created.</param>
		/// <param name="purchaseParams">Purchase parameters such as <c>country</c>, <c>locale</c>, and <c>currency</c>.</param>
		/// <param name="platform">Publishing platform the user plays on.<br/>
		///     Can be `xsolla` (default), `playstation_network`, `xbox_live`, `pc_standalone`, `nintendo_shop`, `google_play`, `app_store_ios`, `android_standalone`, `ios_standalone`, `android_other`, `ios_other`, or `pc_other`.</param>
		/// <param name="customHeaders">Custom HTTP request headers.</param>
		/// <param name="sdkType">SDK type. Used for internal analytics.</param>
		public static void PurchaseForVirtualCurrency(XsollaSettings settings, string itemSku, string priceSku, Action<OrderStatus> onSuccess, Action<Error> onError, Action<OrderId> onOrderCreated = null, PurchaseParams purchaseParams = null, string platform = null, Dictionary<string, string> customHeaders = null, SdkType sdkType = SdkType.Store)
		{
			CreateOrderByVirtualCurrency(
				settings,
				itemSku,
				priceSku,
				orderId => {
					onOrderCreated?.Invoke(orderId);

					OrderTrackingService.AddOrderForTracking(
						settings,
						orderId.order_id,
						false,
						status => OrderStatusService.GetOrderStatus(settings, orderId.order_id, onSuccess, onError, sdkType),
						onError,
						sdkType);
				},
				onError,
				purchaseParams,
				platform,
				customHeaders);
		}

		/// <summary>
		/// Launches purchase process for a specified free item. This method encapsulates methods for creating an order and tracking the order status.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/promo/free-items/).</remarks>
		/// <param name="itemSku">Desired free item SKU.</param>
		/// <param name="onSuccess">Called after the order transitions to the 'done' status.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="onOrderCreated">Called after the order is created.</param>
		/// <param name="purchaseParams">Purchase parameters such as <c>country</c>, <c>locale</c>, and <c>currency</c>.</param>
		/// <param name="customHeaders">Custom HTTP request headers.</param>
		/// <param name="sdkType">SDK type. Used for internal analytics.</param>
		public static void PurchaseFreeItem(XsollaSettings settings, string itemSku, Action<OrderStatus> onSuccess, Action<Error> onError, Action<OrderId> onOrderCreated = null, PurchaseParams purchaseParams = null, Dictionary<string, string> customHeaders = null, SdkType sdkType = SdkType.Store)
		{
			CreateOrderWithFreeItem(
				settings,
				itemSku,
				orderId => {
					onOrderCreated?.Invoke(orderId);

					OrderTrackingService.AddOrderForTracking(
						settings,
						orderId.order_id,
						false,
						status => OrderStatusService.GetOrderStatus(settings, orderId.order_id, onSuccess, onError, sdkType),
						onError,
						sdkType);
				},
				onError,
				purchaseParams,
				customHeaders);
		}

		/// <summary>
		/// [Obsolete. Use GetItems instead.] Returns a list of virtual items according to pagination settings.
		/// The list includes items for which display in the store is enabled in the settings. For each virtual item, complete data is returned.
		/// If used after user authentication, the method returns items that match the personalization rules for the current user.
		/// <b>Attention:</b> The number of items returned in a single response is limited. <b>The default and maximum value is 50 items per response</b>. To get more data page by page, use <code>limit</code> and <code>offset</code> fields.
		/// </summary>
		/// <remarks>[More about the use cases](https://developers.xsolla.com/sdk/unity/catalog/catalog-display/).</remarks>
		/// <param name="onSuccess">Called after virtual items were successfully received.</param>
		/// <param name="onError">Called after the request resulted with an error.</param>
		/// <param name="limit">Limit for the number of elements on the page. The maximum number of elements on a page is 50.</param>
		/// <param name="offset">Number of the element from which the list is generated (the count starts from 0).</param>
		/// <param name="locale">Response language. [Two-letter lowercase language code](https://developers.xsolla.com/doc/pay-station/features/localization/). Leave empty to use the default value.</param>
		/// <param name="country">Country for which to calculate regional prices and restrictions in a catalog. Two-letter uppercase country code per [ISO 3166-1 alpha-2](https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2). Calculations are based on the user's IP address if the country is not specified.  Check the documentation for detailed information about [countries supported by Xsolla](https://developers.xsolla.com/doc/in-game-store/references/supported-countries/).</param>
		/// <param name="additionalFields">The list of additional fields. These fields will be in a response if you send them in a request. Available fields `media_list`, `order`, and `long_description`.</param>
		[Obsolete("Use GetItems instead.")]
		public static void GetCatalog(XsollaSettings settings, Action<StoreItems> onSuccess, Action<Error> onError, int limit = 50, int offset = 0, string locale = null, string country = null, string additionalFields = "long_description")
		{
			var url = new UrlBuilder($"{BaseUrl}/{settings.StoreProjectId}/items/virtual_items")
				.AddLimit(limit)
				.AddOffset(offset)
				.AddLocale(locale)
				.AddCountry(country)
				.AddAdditionalFields(additionalFields)
				.Build();

			WebRequestHelper.Instance.GetRequest(
				SdkType.Store,
				url,
				WebRequestHeader.AuthHeader(settings),
				onSuccess,
				error => TokenAutoRefresher.Check(settings, error, onError, () => GetCatalog(settings, onSuccess, onError, limit, offset, locale, country, additionalFields)),
				ErrorGroup.ItemsListErrors);
		}
		
		public static void PurchaseWithWebshop(XsollaSettings settings, string itemSku, string webshopUrl, string userId, string redirectUrl, Action<OrderStatus> onSuccess, Action<Error> onError, PurchaseParams purchaseParams = null, SdkType sdkType = SdkType.Store)
		{
			if (string.IsNullOrEmpty(webshopUrl))
			{
				onError?.Invoke(new Error(errorMessage: "Webshop URL is empty."));
				return;
			}
			
			if (string.IsNullOrEmpty(userId))
			{
				onError?.Invoke(new Error(errorMessage: "Webshop User Id is empty."));
				return;
			}
			
			var url = new UrlBuilder($"{webshopUrl}")
				.AddUserId(userId)
				.AddPurchaseSku(itemSku)
				.AddRedirectUri(redirectUrl)
				.AddWebshopContent(purchaseParams?.use_buy_button_solution == true ? "buy_button" : null)
				.Build();
			
			XsollaWebBrowser.OpenWebshop(url);

			if (settings.EventApiEnabled)
			{
				OrderTrackingService.AddOrderForTrackingBySku(
					settings,
					itemSku,
					true,
					status =>
					{
						EventManagerProvider.Handler?.BroadcastEvent(EventTypes.PaystationCompleted);
						XsollaWebBrowser.Close();
						onSuccess?.Invoke(status);
					},
					onError: error =>
					{
						if (error.ErrorType == ErrorType.UserCancelled)
							EventManagerProvider.Handler?.BroadcastEvent(EventTypes.PaystationCancelled);
							
						onError?.Invoke(error);
					},
					sdkType);
			}
		}
		
		public static void PurchaseWithToken(XsollaSettings settings, string paymentToken, Action<OrderStatus> onSuccess, Action<Error> onError, Action<OrderData> onOrderCreated = null, Action<BrowserCloseInfo> onBrowseClosed = null, PurchaseParams purchaseParams = null, Dictionary<string, string> customHeaders = null, PlatformSpecificAppearance platformSpecificAppearance = null, SdkType sdkType = SdkType.Store)
		{
			if (string.IsNullOrEmpty(paymentToken))
			{
				onError?.Invoke(new Error(errorMessage: "Payment token is empty."));
				return;
			}
			
			XsollaWebBrowser.OpenPurchaseUI(
				settings,
				paymentToken,
				false,
				onBrowseClosed,
				platformSpecificAppearance,
				sdkType
			);

			OrderTrackingService.AddOrderForTrackingByPaymentToken(
				settings,
				token: paymentToken,
				true,
				status =>
				{
					EventManagerProvider.Handler?.BroadcastEvent(EventTypes.PaystationCompleted);
					
					XsollaWebBrowser.Close();
					if (string.IsNullOrEmpty(status.transaction_id))
						OrderStatusService.GetOrderInfo(
							settings,
							accessToken: paymentToken,
							sdkType: sdkType,
							onSuccess: orderInfo =>
							{
								if (orderInfo.TryAsDone(out var done))
								{
									status.transaction_id = done.invoiceId.ToString();
									onSuccess(status);
								}
								else
								{
									onError.Invoke(new Error(errorMessage: $"Wrong post payment order status, expected 'done' (payment_token={paymentToken})"));
								}
							},
							onFailure: err =>
							{
								onError.Invoke(new Error(errorMessage: $"Post payment order status query failed (payment_token={paymentToken}):\n{err}"));
							}
						);
					else
						onSuccess?.Invoke(status);
				},
				onError: error => 
				{
					if (error.ErrorType == ErrorType.UserCancelled)
						EventManagerProvider.Handler?.BroadcastEvent(EventTypes.PaystationCancelled);
							
					if (error.ErrorType == ErrorType.OrderInfoDoneButInvalidOrderId 
					    && purchaseParams != null && purchaseParams.allow_token_only_finished_status_without_orderId
					) {
						if (error.AdditionalData != null && error.AdditionalData.ContainsKey("invoice_id"))
						{
							var orderStatus = new OrderStatus
							{
								order_id = 0,
								status = "done",
								transaction_id = error.AdditionalData["invoice_id"].ToString()
							};
							
							onSuccess?.Invoke(orderStatus);
						}
						else
							onError?.Invoke(error);
					}
					else
						onError?.Invoke(error);
				},
				sdkType
			);
		}
	}
}