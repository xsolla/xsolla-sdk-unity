using System;
using System.Collections.Generic;

namespace Xsolla.Core
{
	internal static class OrderStatusService
	{
		/// <summary>
		/// Performs the order-status web request, cache aside.
		/// <para/>
		/// Production keeps <see cref="PerformWebRequest"/>. Tests swap it to script statuses for the order trackers without
		/// a backend. The token-refresh retry inside <see cref="PerformWebRequest"/> deliberately calls the real request.
		/// </summary>
		internal static Action<XsollaSettings, long, string, Action<OrderStatus>, Action<Error>, SdkType> OrderStatusRequester = PerformWebRequest;

		/// <summary>
		/// Performs the payment-status web request of a payment token.
		/// <para/>
		/// Production keeps <see cref="PerformOrderInfoRequest"/>. Tests swap it to script payment statuses for the order
		/// trackers without a backend. The token-refresh retry inside <see cref="PerformOrderInfoRequest"/> deliberately calls
		/// the real request.
		/// </summary>
		internal static Action<XsollaSettings, string, SdkType, Action<OrderInfo>, Action<Error>> OrderInfoRequester = PerformOrderInfoRequest;

		public static void GetOrderStatus(XsollaSettings settings, long orderId, Action<OrderStatus> onSuccess, Action<Error> onError, SdkType sdkType = SdkType.Store, string token = null)
		{
			if (OrderStatusCache.TryPerform(orderId, onSuccess))
				return;

			OrderStatusRequester(
				settings,
				orderId,
				token,
				status =>
				{
					OrderStatusCache.UpdateStatus(status);
					onSuccess?.Invoke(status);
				},
				onError,
				sdkType);
		}

		private static void PerformWebRequest(XsollaSettings settings, long orderId, string token, Action<OrderStatus> onSuccess, Action<Error> onError, SdkType sdkType)
		{
			var url = $"https://store.xsolla.com/api/v2/project/{settings.StoreProjectId}/order/{orderId}";

			WebRequestHelper.Instance.GetRequest(
				sdkType,
				url,
				(!string.IsNullOrEmpty(token) ? WebRequestHeader.AuthHeader(token): WebRequestHeader.AuthHeader(settings)),
				onSuccess,
				error => TokenAutoRefresher.Check(settings, error, onError, () => PerformWebRequest(settings, orderId, token, onSuccess, onError, sdkType)),
				ErrorGroup.OrderStatusErrors);
		}
		
		public static void GetOrderInfo(
            XsollaSettings settings,
            [System.Diagnostics.CodeAnalysis.NotNull]
            string accessToken, SdkType sdkType,
            Action<OrderInfo> onSuccess, Action<Error> onFailure
        )
        {
            if (string.IsNullOrEmpty(accessToken)) {
                onFailure.Invoke(new Error(
                    errorType: ErrorType.OrderInfoWrongAccessToken, 
                    errorMessage: "Access token is null or empty"
                ));

                return;
            }

            OrderInfoRequester(settings, accessToken, sdkType, onSuccess, onFailure);
        }

		private static void PerformOrderInfoRequest(XsollaSettings settings, string accessToken, SdkType sdkType, Action<OrderInfo> onSuccess, Action<Error> onFailure)
        {
            var url = PayStationUrlBuilder.GetPaystationHost(settings) + $"paystation2/api/payments/status?access_token={accessToken}";

            XDebug.Log(settings, $"[OrderInfo.Query] url={LogRedaction.Redact(url)}");

            WebRequestHelper.Instance.GetRequest<OrderInfo.Response>(sdkType, url, requestHeader: null,
                onComplete: response =>
                {
                    if (OrderInfo.TryFromResponse(response, out var orderInfo, out var error))
                        onSuccess.Invoke(orderInfo);
                    else
                        onFailure.Invoke(error);
                },
                onError: error =>
                {
                    TokenAutoRefresher.Check(settings, error,
                        onError: error =>
                        {
                            onFailure.Invoke(new Error(
                                errorType: ErrorType.OrderInfoUnreachable, 
                                errorMessage: "Failed to reach order info service: " + error
                            ));
                        },
                        onSuccess: () => PerformOrderInfoRequest(settings, accessToken, sdkType, onSuccess, onFailure)
                    );
                }
            );
        }
	}
}