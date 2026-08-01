# Xsolla Mobile SDK for Unity

![License](https://img.shields.io/github/license/xsolla/xsolla-sdk-unity)
![Latest release](https://img.shields.io/github/v/release/xsolla/xsolla-sdk-unity)
[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-orange.svg)](https://unity.com)
[![C#](https://img.shields.io/badge/C%23-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![UPM compatible](https://img.shields.io/badge/UPM-compatible-brightgreen.svg)](https://docs.unity3d.com/Manual/upm-ui.html)

Pre-built Unity SDK for integrating in-game payments into your app via Xsolla Pay Station.

## SDK Explorer

See exactly how payments work before writing a single line of code. The SDK Explorer lets you walk through authentication, catalog loading, purchasing, and finalization — all in an interactive environment.

[![SDK Explorer — interactive demo of Xsolla Mobile SDK payment flow](readme-assets~/explorer.png)](https://developers.xsolla.com/sdk/demo/)

[**Integrate Now →**](https://developers.xsolla.com/sdk/demo/)

## Essential Links

- [SDK Explorer](https://developers.xsolla.com/sdk/demo/) — interactive demo
- [SDK Documentation](https://developers.xsolla.com/sdk/) — full integration guide
- [Demo App](https://github.com/xsolla/xsolla-sdk-demo) — sample project

## Overview

Xsolla Mobile SDK provides a Unity IAP 5 custom store for in-game purchases via Xsolla Pay Station. It uses the service-based `StoreController`, product, and order APIs introduced in Unity IAP 5.

**Key features:**

- 1000+ payment methods across 200+ geographies
- 130+ currencies including local and alternative payment methods
- Built-in anti-fraud protection
- 25+ languages supported out of the box
- Player authentication (Xsolla Login widget, social login, custom tokens)
- Product catalog and virtual items
- Buy Button and Web Shop integration

## Requirements

- Unity 2022.3 LTS or later
- Unity IAP (`com.unity.purchasing`) 5.0.2+

## Installation

Add the package via Unity Package Manager using the Git URL:

1. In Unity, go to **Window > Package Manager**
2. Click **+** > **Add package from git URL...**
3. Enter:
   ```
   https://github.com/xsolla/xsolla-sdk-unity.git
   ```
4. Click **Add**

## Quick Start

### 1. Connect

Configure the SDK, register Xsolla, subscribe to Unity IAP 5 events, and connect to the store:

```csharp
using UnityEngine;
using UnityEngine.Purchasing;
using Xsolla.SDK.Common;
using Xsolla.SDK.UnityPurchasing;
using System.Collections.Generic;

var settings = XsollaClientSettings.Builder.Create()
    .SetProjectId(77640)
    .SetLoginId("026201e3-7e40-11ea-a85b-42010aa80004")
    .Build();

var configuration = XsollaClientConfiguration.Builder.Create()
    .SetSettings(settings)
    .SetSandbox(true)
    .Build();

var module = XsollaPurchasingModule.Builder.Create()
    .SetConfiguration(configuration)
    .Build();

StoreController storeController = module.CreateStoreController();
IXsollaPurchasingStoreExtension xsolla = module.GetStoreExtension();

storeController.OnProductsFetched += OnProductsFetched;
storeController.OnProductsFetchFailed += OnProductsFetchFailed;
storeController.OnPurchasePending += OnPurchasePending;
storeController.OnPurchaseFailed += OnPurchaseFailed;
storeController.OnPurchaseConfirmed += OnPurchaseConfirmed;
storeController.OnPurchasesFetched += OnPurchasesFetched;

await storeController.Connect();

storeController.FetchProducts(new List<ProductDefinition>
{
    new ProductDefinition("com.xsolla.crystals.10", ProductType.Consumable)
});
```

### 2. Handle Products

Unity IAP 5 returns fetched products through `OnProductsFetched`. Fetch existing purchases after products are available so restored orders can be matched to their product definitions:

```csharp
private void OnProductsFetched(List<Product> products)
{
    storeController.FetchPurchases();
}

private void OnProductsFetchFailed(ProductFetchFailed failure)
{
    Debug.LogError(failure.FailureReason);
}

private void OnPurchasesFetched(Orders orders)
{
    // Unity IAP 5 exposes restored consumables here as pending orders.
    foreach (PendingOrder order in orders.PendingOrders)
        ProcessPendingOrder(order);

    // Restore entitlements represented by orders.ConfirmedOrders here too.
}
```

### 3. Purchase

Initiate a purchase using the Unity IAP 5 controller:

```csharp
Product product = storeController.GetProductById("com.xsolla.crystals.10");
storeController.PurchaseProduct(product);
```

### 4. Finalize

Handle the pending order, validate its receipt, award the product, and confirm it. Confirming a consumable calls the Xsolla consume operation; the order is confirmed only after that operation succeeds:

```csharp
private void OnPurchasePending(PendingOrder order)
{
    ProcessPendingOrder(order);
}

private void ProcessPendingOrder(PendingOrder order)
{
    xsolla.GetValidator().Validate(order.Info.Receipt, (success, error) =>
    {
        if (success)
        {
            // Award the product to the user
            storeController.ConfirmPurchase(order);
        }
    });
}

private void OnPurchaseFailed(FailedOrder order)
{
    Debug.LogError($"Purchase failed: {order.FailureReason}: {order.Details}");
}

private void OnPurchaseConfirmed(Order order)
{
    // Confirmation completed, or inspect FailedOrder if confirmation failed.
}
```

> For the full integration guide, see the [SDK Documentation](https://developers.xsolla.com/sdk/).

## Support

- **GitHub Issues:** [github.com/xsolla/xsolla-sdk-unity/issues](https://github.com/xsolla/xsolla-sdk-unity/issues)
- **Developer portal:** [developers.xsolla.com](https://developers.xsolla.com)

## License

Apache 2.0 License. See [LICENSE](./LICENSE).
