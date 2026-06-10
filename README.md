# Xsolla Mobile SDK for Unity

[![License](https://img.shields.io/github/license/xsolla/xsolla-sdk-unity)](./LICENSE)
[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-orange.svg)](https://unity.com)
[![C#](https://img.shields.io/badge/C%23-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![UPM compatible](https://img.shields.io/badge/UPM-compatible-brightgreen.svg)](https://docs.unity3d.com/Manual/upm-ui.html)

## Overview

Xsolla Mobile SDK for Unity is a pre-built SDK for integrating in-game payments into your app via Xsolla Pay Station. It provides a Unity IAP-compatible purchasing module that plugs into Unity's standard `IStoreListener` pattern, so the purchase flow feels familiar to Unity developers.

**Key features:**

- 1000+ payment methods across 200+ geographies
- 130+ currencies, including local and alternative payment methods
- Built-in anti-fraud protection
- 25+ languages out of the box
- Player authentication (Xsolla Login widget, social login, custom tokens)
- Product catalog and virtual items
- Buy Button and Web Shop integration

Try the [interactive SDK Explorer](https://developers.xsolla.com/sdk/demo/) to see the payment flow before writing code.

## Requirements

- Unity 2022.3 LTS or later
- Unity IAP (`com.unity.purchasing`) 4.13.0+ (5.x is not supported)

## Install

Add the package via Unity Package Manager using the Git URL:

1. In Unity: **Window > Package Manager**
2. Click **+** > **Add package from git URL...**
3. Enter: `https://github.com/xsolla/xsolla-sdk-unity.git`
4. Click **Add**

## Usage

```csharp
using UnityEngine.Purchasing;
using Xsolla.SDK.Common;
using Xsolla.SDK.UnityPurchasing;

// 1. Connect — configure and initialize Unity Purchasing
var settings = XsollaClientSettings.Builder.Create()
    .SetProjectId(77640)
    .SetLoginId("026201e3-7e40-11ea-a85b-42010aa80004")
    .Build();
var configuration = XsollaClientConfiguration.Builder.Create()
    .SetSettings(settings).SetSandbox(true).Build();
var module = XsollaPurchasingModule.Builder.Create()
    .SetConfiguration(configuration).Build();

var builder = ConfigurationBuilder.Instance(module);
builder.AddProduct("com.xsolla.crystals.10", ProductType.Consumable);
UnityPurchasing.Initialize(this, builder); // `this` implements IStoreListener

// 2. On OnInitialized, store the controller + Xsolla extension
// 3. Purchase
_storeController.InitiatePurchase(_storeController.products.WithID("com.xsolla.crystals.10"));

// 4. Finalize in ProcessPurchase — validate the receipt, then:
_storeController.ConfirmPendingPurchase(args.purchasedProduct);
```

See the [SDK Documentation](https://developers.xsolla.com/sdk/) for the full integration guide.

## Documentation

- [SDK Documentation](https://developers.xsolla.com/sdk/) — full integration guide
- [SDK Explorer](https://developers.xsolla.com/sdk/demo/) — interactive demo
- [Demo App](https://github.com/xsolla/xsolla-sdk-demo) — sample project

## Support

- **GitHub Issues:** [github.com/xsolla/xsolla-sdk-unity/issues](https://github.com/xsolla/xsolla-sdk-unity/issues)
- **Integration team:** integration@xsolla.com
- **Developer portal:** [developers.xsolla.com](https://developers.xsolla.com)

## License

Apache License 2.0. See [LICENSE](./LICENSE).
