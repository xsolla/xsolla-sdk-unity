# Xsolla Mobile SDK for Unity

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

Xsolla Mobile SDK provides a Unity IAP-compatible purchasing module for in-game purchases via Xsolla Pay Station. It integrates with Unity's standard `IStoreListener` pattern so the purchase flow feels familiar to Unity developers.

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
- Unity IAP (`com.unity.purchasing`) 4.13.0+ (5.x is not supported)

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

Configure the SDK with your project credentials, set up a purchase listener (see step 4), and initialize Unity Purchasing:

```csharp
using UnityEngine;
using UnityEngine.Purchasing;
using Xsolla.SDK.Common;
using Xsolla.SDK.UnityPurchasing;

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

var builder = ConfigurationBuilder.Instance(module);
builder.AddProduct("com.xsolla.crystals.10", ProductType.Consumable);
// ...more products

UnityPurchasing.Initialize(this, builder); // `this` implements IStoreListener
```

### 2. Handle Initialization

Store the controller and Xsolla extensions when Unity Purchasing is ready:

```csharp
private IStoreController _storeController;
private IXsollaPurchasingStoreExtension _xsollaExtensions;

public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
{
    _storeController = controller;
    _xsollaExtensions = extensions.GetExtension<IXsollaPurchasingStoreExtension>();

    // Products are now available via controller.products.all
}

public void OnInitializeFailed(InitializationFailureReason error, string message)
{
    // Handle error
}
```

### 3. Purchase

Initiate a purchase using the store controller:

```csharp
Product product = _storeController.products.WithID("com.xsolla.crystals.10");
_storeController.InitiatePurchase(product);
```

### 4. Finalize

Handle completed transactions in `ProcessPurchase`, validate the receipt, and confirm:

```csharp
public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
{
    _xsollaExtensions.GetValidator().Validate(args.purchasedProduct.receipt, (success, error) =>
    {
        if (success)
        {
            // Award the product to the user
            _storeController.ConfirmPendingPurchase(args.purchasedProduct);
        }
    });

    return PurchaseProcessingResult.Pending;
}

public void OnPurchaseFailed(Product product, PurchaseFailureReason reason)
{
    // Handle error
}
```

> For the full integration guide, see the [SDK Documentation](https://developers.xsolla.com/sdk/).