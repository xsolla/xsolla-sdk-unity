using System;
using UnityEngine;

namespace Xsolla.Core
{
	internal class PayStationUrlBuilder
	{
		private readonly string PaymentToken;
		private readonly SdkType SdkType;
		private readonly bool IsSandBox;
		private readonly int PayStationVersion;

		public PayStationUrlBuilder(XsollaSettings settings, string paymentToken, SdkType sdkType = SdkType.Store)
		{
			PaymentToken = paymentToken;
			SdkType = sdkType;
			IsSandBox = settings.IsSandbox;
			PayStationVersion = settings.PaystationVersion;
		}

		public string Build(XsollaSettings settings)
		{
			var url = GetUrl(settings);

			return new UrlBuilder(url)
				.AddParam(GetTokenQueryKey(), PaymentToken)
				.AddParam("engine", "unity")
				.AddParam("engine_v", Application.unityVersion)
				.AddParam("sdk", WebRequestHelper.GetSdkType(SdkType))
				.AddParam("sdk_v", Info.SDK_VERSION)
				.AddParam("browser_type", GetBrowserType(settings))
				.AddParam("build_platform", GetBuildPlatform())
				.Build();
		}
		
		private string GetUrl(XsollaSettings settings)
		{
			return new UriBuilder(GetPaystationBasePath(settings)).Uri + GetPaystationVersionPath();
		}
		
		private static string GetPaystationCustomHost(XsollaSettings settings, bool? isSandbox = null)
		{
			var isSandbox_ = isSandbox ?? settings.IsSandbox;
			if (isSandbox_
			    && !string.IsNullOrEmpty(settings.CustomPayStationDomainSandbox)
			    && Uri.IsWellFormedUriString(settings.CustomPayStationDomainSandbox, UriKind.Absolute)
			   )
			{
				return settings.CustomPayStationDomainSandbox;
			}
			if (!isSandbox_
			    && !string.IsNullOrEmpty(settings.CustomPayStationDomainProduction)
			    && Uri.IsWellFormedUriString(settings.CustomPayStationDomainProduction, UriKind.Absolute)
			   )
			{
				return settings.CustomPayStationDomainProduction;
			}

			return "";
		}
	    public static string GetPaystationHost(XsollaSettings settings, bool? isSandbox = null)
	    {
	        var isSandbox_ = isSandbox ?? settings.IsSandbox;
	        var prefix = isSandbox_ ? "sandbox-" : string.Empty;
	        
	        var customHost = GetPaystationCustomHost(settings, isSandbox);
	        
	        if (!string.IsNullOrEmpty(customHost))
		        return customHost.TrimEnd('/') + "/";
	        
	        return $"https://{prefix}secure.xsolla.com/";
	    }

		private string GetPaystationBasePath(XsollaSettings settings)
		{
			return GetPaystationHost(settings, IsSandBox);
		}

		private string GetPaystationVersionPath()
		{
			switch (PayStationVersion)
			{
				case 3:  return "paystation3";
				case 4:  return "paystation4";
				default: throw new Exception($"Unknown Paystation version: {PayStationVersion}");
			}
		}

		private string GetTokenQueryKey()
		{
			switch (PayStationVersion)
			{
				case 3:  return "access_token";
				case 4:  return "token";
				default: throw new Exception($"Unknown PayStation version: {PayStationVersion}");
			}
		}

		private string GetBrowserType(XsollaSettings settings)
		{
			return settings.InAppBrowserEnabled
				? "inapp"
				: "system";
		}

		private string GetBuildPlatform()
		{
			return Application.platform.ToString().ToLowerInvariant();
		}
	}
}