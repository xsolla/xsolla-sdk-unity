# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.0.39] - 01-04-2026

### Changed

- Updated `Payments`: 1.4.20 -> 1.4.21

### Fixed

- Fixed PayStation WebView not recovering from main-frame load errors and connectivity changes
- Fixed stale payment redirect pulling the app to the foreground when another payment flow is active
- Fixed redirect nonce not appended to custom redirect URLs (only the default deep-link URL needs it)
- Fixed `paymentFlowActive` flag shared across `BillingClient` instances causing incorrect redirect routing when multiple instances coexist; active flows are now tracked per-nonce in `PaymentFlowRegistry`

## [3.0.38] - 24-03-2026

### Added

- Added opt-in app relaunch from PayStation redirect on cold start (`ConfigWithoutIntegration.Payments::withRedirectAppRelaunch`). When the app is killed during a payment flow in an external browser, tapping "Back to Game" in PayStation now relaunches the app instead of silently failing.

## [3.0.37] - 17-03-2026

### Added

- Added optional cancellation reason querying (`ConfigWithoutIntegration.Payments::withQueryCancellationReasonEnabled`) to distinguish between clean cancels and failed payments

### Fixed

- Fixed token-based billing flow not checking invoice status after activity result

## [3.0.36] - 12-03-2026

### Changed

- Made `ActivityUtils` public

### Fixed

- Fixed an infinite loop in `forJWT` Config method
- Fixed a bug where the payment flow could get stuck when the proxy activity is destroyed externally

## [3.0.35] - 03-02-2026

### Changed

- Updated `Payments`: 1.4.19 -> 1.4.20 (update blacklisted browser list, etc).
- Updated `Login`: 6.0.17 -> 6.0.18

## [3.0.34] - 22-01-2026

### Fixed

- A bug that would result in string formatter sometimes incorrectly interpolating the arguments

### Changed

- Improved retryable asynchronous method error handling

## [3.0.33] - 21-01-2026

### Added

- Added support for payment flow cancellation

## [3.0.32] - 14-01-2026

### Added

- Added support for domain overriding (payments, see `ConfigWithoutIntegration.Payments.DomainOverrideConfig`)

### Changed

- Updated `Payments`: 1.4.18 -> 1.4.19

## [3.0.31] - 13-01-2026

### Changed

- Updated `Payments`: 1.4.17 -> 1.4.18 (support for navigation events in custom tabs based payment activity)

## [3.0.30] - 30-12-2025

### Added

- Added support for manual JWT token updating (`Config.Integration.Xsolla.ForJWT` + `JWTRefresher`)

## [3.0.29] - 30-12-2025

### Added

- Added support for navigation events for payment activity (`ConfigWithoutIntegration.Payments.EventListeners`)

### Changed

- Updated `Payments`: 1.4.16 -> 1.4.17

## [3.0.28] - 29-12-2025

### Added

- Added support for logo visibility setting in `Config.Payments`

## [3.0.27] - 10-12-2025

### Added

- Added `ProviderUtils` helper (browser related utilities)
- Added e-mail consent opt-in setting (`Config.Payments::withEmailCollectionConsentOptInEnabled`)
- Added config log dumping on billing client creation in debug mode
- Implemented provider blacklisting, prioritization, etc settings for `Config.Payments.CustomTabs` and `Config.Payments.TrustedWebActivity`
- Implemented overridable retry policy settings (see `Config.Common::withRetryPolicies` and `RetryPolicies`) for all asynchronous server requests

### Fixed

- Improved compatibility with some of the browsers that wouldn't correctly redirect back into app (e.g. `Amazon Silk`)
- Blacklisted `Microsoft Edge` from being a compatible provider
- Blacklisted `Samsung Internet` from being a compatible TWA provider (has a partial, broken implementation)
- Fixed custom tabs provider initialization order (controlled now)

### Changed

- Updated `Store`: 2.5.14 -> 2.5.15
- Updated `Payments`: 1.4.15 -> 1.4.16
- Moved out `RetryProfile` from `Common.Payments`
- Moved `PendingOrderStatusQueryRetryProfileOverride` setting from `Common.Payments` to `RetryPolicies`
- Removed `-keepattributes *Annotation*` from the user-facing `consumer-rules.pro`

## [3.0.26] - 02-12-2025

### Added

- Support for `browser_type` field inside payment tokens

## [3.0.25] - 28-11-2025

### Fixed

- Redirect URL fixes

## [3.0.24] - 24-11-2025

### Changed

- Improved logging

## [3.0.23] - 21-11-2025

### Added

- Support for `install_source` parameter in payment tokens

### Changed

- Updated `Store` library: 2.5.13 -> 2.5.14

## [3.0.22] - 20-11-2025

### Changed

- Improved payment flow cancellation detection algorithm in certain situations
- Reduced the default number of attempts allowed for querying stuck payment flow status (20 -> 12)

## [3.0.21] - 17-11-2025

### Added

- Added `invokePurchasesUpdatedForMissingOrderId` enables the `PurchasesUpdatedListener.onPurchasesUpdated()` invocation on purchases with a missing order ID (based on customized payment tokens)

## [3.0.20] - 14-11-2025

### Changed

- Improved invoice status querying for both standard and token based flows

### Fixed

- Fixed logger not respecting log levels in certain scenarios

## [3.0.19] - 12-11-2025

### Added

- Support for `country` field when creating a payment token based on the locale override

## [3.0.18] - 07-11-2025

### Fixed

- WebShop URL parameter name fix

## [3.0.17] - 06-11-2025

### Fixed

- CustomTabs and TrustedWebActivity related fixes

### Changed

- Updated `Payments` library: 1.4.13 -> 1.4.15

## [3.0.16] - 21-10-2025

### Fixed

- Redirect button text can now be customized through PA settings

## [3.0.15] - 20-10-2025

### Added

- Added support for customized payment tokens (the result is handled on the backend)

## [3.0.14] - 15-10-2025

### Added

- Ability to control via a flag whether only personalized products are fetched from the catalog

## [3.0.13] - 07-10-2025

### Added

- Ability to enable Buy Button solution for billing flows

### Changed

- Updated `Store` library: 2.5.12 -> 2.5.13

## [3.0.12] - 30-09-2025

### Fixed

- Improved order status querying retry profiles

### Added

- Ability to override the order status querying retry profile using the payments config

## [3.0.11] - 23-09-2025

### Fixed

- Order status querying fix for billing flows launched using an externally generated purchase token

## [3.0.10] - 22-09-2025

### Added

- Advanced events manager scheduling

### Changed

- Updated `Payments` library: 1.4.12 -> 1.4.13
- Updated `Login` library: 6.0.16 -> 6.0.17

## [3.0.9] - 10-09-2025

### Fixed

- Ability to open TWA without a splash screen

## [3.0.8] - 09-09-2025

### Added

- Added support for 'free' purchases, i.e. purchases that have no cost

## [3.0.7] - 01-09-2025

### Added

- Extended `IntegrationUtils` with additional utility methods

## [3.0.6] - 29-08-2025

### Changed

- Updated `Payments` library: 1.4.11 -> 1.4.12

## [3.0.5] - 28-08-2025

### Added

- Google Play store's country code can now be queried using `IntegrationUtils.queryGooglePlayCountryCodeAsync()` utility method

## [3.0.4] - 25-08-2025

### Fixed

- Focus monitor would sometimes crash under certain circumstances
- Fixed events API payload parsing

## [3.0.3] - 21-08-2025

### Added

- Added support for a custom project ID in Xsolla Events API

## [3.0.2] - 18-08-2025

### Fixed

- Fixed pending order status querying during the billing flow

## [3.0.1] - 15-08-2025

### Added

- Added support for the Webshop oriented billing flows
- Added the utility helper for opening URLs in an external browser

## [3.0.0] - 12-08-2025

### Changed

- Promoted the SDK to a **major release**. This release marks the end of the alpha series
- Updated `Payments` library 1.4.9 -> 1.4.11

### Added

- Added utilities for acquiring authentication tokens through social network access tokens 
- Added support for social access token authentication method
- Logging for anything that relies on retry logic

## [2.1.26-alpha] - 05-08-2025

### Changed

- Xsolla Events API improvements
- Purchase token generation improvements (compatibility with older tokens is not affected)
- Updated the `Payments` library 1.4.8 -> 1.4.9

### Fixed

- Additional checks for non-compatible browser apps

## [2.1.25-alpha] - 24-07-2025

### Added

- Added support for Xsolla Events API when dealing with deferred purchases

## [2.1.24-alpha] - 22-07-2025

### Added

- Added support for payment method ID when launching a billing flow

## [2.1.23-alpha] - 22-07-2025

### Added

- Added support for launching the billing flow using a pre-generated payment token

## [2.1.22-alpha] - 16-07-2025

### Added

- Added support for `tracking ID` when launching a billing flow

## [2.1.21-alpha] - 15-07-2025

### Changed

- Order status is now forcefully checked whenever the payment activity closes with an unexpected result

## [2.1.20-alpha] - 20-06-2025

### Changed

- Updated `Login` library 6.0.14 -> 6.0.16

## [2.1.19-alpha] - 13-06-2025

### Added

- `queryPurchasesAsync` now also returns unconsumed product instances

## [2.1.18-alpha] - 12-06-2025

### Fixed

- Proper payment cancellation tracking for non-ChromeTab-based activities

## [2.1.17-alpha] - 11-06-2025

### Added

- Added support for `AccountIdentifiers` and exposed the related functionality

### Changed

- Moved from `BillingFlowParams.ProductDetailsParams` to `BillingFlowParams`:
	- `setCustomPayload`
	- `setExternalId`
- Renamed in `BillingFlowParams`:
	- `setCustomPayload` -> `setDeveloperPayload`
	- `setExternalId` -> `setExternalTransactionId`
- Removed support in `BillingFlowParams.ProductDetailsParams.Builder` for:
	- `setOfferToken`
- Removed support in `BillingFlowParams.Builder` for:
	- `setIsOfferPersonalized`
- Removed support in `ProductDetails` for:
	- `RecurrenceMode`
	- `PricingPhase`
	- `PricingPhases`
	- `SubscriptionOfferDetails`
	- `getSubscriptionOfferDetails`
	
### Fixed

- Eliminated redundant characters in purchase tokens

## [2.1.16-alpha] - 09-06-2025

### Changed

- Added support for `external ID` for launched billing flows
- Improved error reporting on billing flow initialization

## [2.1.15-alpha] - 03-06-2025

### Changed

- Improved error message reporting across the SDK

## [2.1.14-alpha] - 10-04-2025

### Changed

- Updated `Payments` library 1.4.7 -> 1.4.8

## [2.1.13-alpha] - 12-03-2025

### Added

- Added more modes for the experimental Xsolla Login Widget

## [2.1.12-alpha] - 27-12-2025

### Added

- Added ability to refresh the login token directly

### Changed

- Updated `Payments` library 1.4.6 -> 1.4.7

## [2.1.11-alpha] - 26-12-2025

### Fixed

- A fix for a potential crash related to dereferencing uninitialized objects

### Changed

- Updated `Payments` library 1.4.5 -> 1.4.6

## [2.1.10-alpha] - 18-02-2025

### Fixed

- An issue that would lead to a broken state due to an abrupt connection loss during the authentication stage

### Changed

- Adjusted the retry parameters for backend-oriented methods:
	- Reduced the number of attempts before an operation is viewed as failed (5 -> 3)
	- Reduced the maximum amount of time between the subsequent attempts with the `exponential` retry method

## [2.1.9-alpha] - 12-02-2025

### Fixed

- An issue that would prevent the payment activity to broadcast the correct billing result

## [2.1.8-alpha] - 10-02-2025

### Fixed

- An issue that would prevent the product prices to be parsed correctly

## [2.1.7-alpha] - 05-02-2025

### Added

- ProGuard rules

### Changed

- Updated `Login` library 6.0.11 -> 6.0.14
- Updated `Store` library 2.5.10 -> 2.5.11
- Updated `Payments` library 1.4.4 -> 1.4.5

## [2.1.6-alpha] - 02-12-2024

### Changed

- Added support for opening payments view in an external system browser (`ConfigWithoutIntegration.Payments.Activity.System`)
- Updated `Payments` library 1.4.3 -> 1.4.4

## [2.1.5-alpha] - 22-11-2024

### Changed

- Updated `Payments` library 1.4.2 -> 1.4.3

## [2.1.4-alpha] - 14-10-2024

### Added

- Added an internal support for external transaction ID in `BillingFlowParams.ProductDetailsParams`

## [2.1.3-alpha] - 08-10-2024

### Added

- Added support for a developer payload using `BillingFlowParams.ProductDetailsParams.setDeveloperPayload()`

## [2.1.2-alpha] - 01-10-2024

### Changed

- Updated `Login` library 6.0.10 -> 6.0.11
- Updated `Payments` library 1.4.0 -> 1.4.2

## [2.1.1-alpha] - 23-09-2024

### Added

- Implemented `getAuthenticationToken`
- Ability to set the custom user ID property via the payments config
- Updated `Login` library 6.0.9 -> 6.0.10

## [2.1.0-alpha] - 06-09-2024

### Added

- Added experimental support for Xsolla Login Widget.

## [2.0.2] - 04-09-2024

### Changed

- Updated `Store` library 2.5.7 -> 2.5.10

## [2.0.1] - 27-08-2024

### Added

- Re-try logic for asynchronous methods (back-off approach).

## [2.0.0] - 14-08-2024

### Changed

- Synced version across the platforms.

## [0.0.11] - 23-07-2024

### Changed

- Changed visibility of some classes.

## [0.0.10] - 19-07-2024

### Added

- Authentication using a `Xsolla Login` widget.

## [0.0.9] - 18-07-2024

### Changed

- Removed dependency on a customized payments library.
- Updated external dependencies.

## [0.0.8] - 18-07-2024

### Fixed

- Fixed `ProxyActivity` visibility.

## [0.0.7] - 18-07-2024

### Fixed

- Fixed a potential crash on purchasing flow launch.

## [0.0.6] - 08-07-2024

### Fixed

- Revised visibility of some classes.

## [0.0.5] - 05-07-2024

### Fixed

- Fixed game engine version not being passed to analytics.

## [0.0.4] - 20-06-2024

### Added

- Introduced `Config.Analytics` for analytics related settings.
- Library version tag and name are now available via `BuildConfig`.

## [0.0.3] - 07-06-2024

### Changed

- Migrated SDK to `com.xsolla.android.mobile` package.
- Made Google Play Billing dependency `compile-only`.

## [0.0.2] - 05-06-2024

### Added

- Introduced a changelog.

## [0.0.1] - 05-06-2024

### Added

- Initial implementation.
