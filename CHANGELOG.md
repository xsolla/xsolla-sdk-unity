## [3.1.10] - 2026-20-04

### Changed

- Android: updated SDK to `3.0.41` (opt-in multi-unit purchase collapsing on restore)

### Added

- Android: Added `collapseRestoredMultiUnitPurchases` setting to `AdvancedSettingsAndroid` — controls whether restored multi-unit SKUs are expanded into individual quantity-1 notifications (default) or reported as a single combined batch; collapse is recommended for high-quantity SKUs such as soft currency
- Android: Native SDK dependency is now resolved from the public GitHub Maven repository (`https://raw.githubusercontent.com/xsolla/xsolla-sdk-android/main`) via EDM4U rather than bundled as a local `.aar`; transitive dependencies are resolved automatically through the POM

## [3.1.9] - 2026-04-15

### Fixed

- iOS: Fixed initialization with WebShop mode enabled

## [3.1.8] - 2026-09-04

### Changed

- Android: updated SDK to `3.0.40` (`AsyncRetryScheduler` exception handling improvements)
- Added `LinuxStandalone64` to `.asmdef`

### Fixed

- iOS: Fixed `RestorePurchases` call during Unity Purchasing initialization causing a failure

## [3.1.7] - 2026-02-04

### Changed

- iOS: updated SDK to `3.9.0` (purchase restoration support)

### Fixed

- iOS: `RestorePurchases` now properly triggers unfinished purchase restoration via the native SDK
- iOS: failed transactions are now finished immediately to prevent them from blocking future restores

## [3.1.6] - 2026-02-04

### Changed

- Android: updated SDK to `3.0.39` (WebView and redirect reliability fixes)

### Added

- Added `SetCustomPayStationProductionDomain` and `SetCustomPayStationSandboxDomain` builder methods — allow overriding the Pay Station domain for a single environment without affecting the other

### Fixed

- Android: Fixed PayStation WebView not recovering from network errors and connectivity changes
- Android: Fixed stale payment redirect bringing the app to the foreground while another payment flow is active
- Android: Fixed redirect nonce not appended to custom redirect URLs
- Android: Fixed payment flow routing when multiple billing client instances coexist

## [3.1.5] - 2026-24-03

### Changed

- Android: updated SDK to `3.0.38` (PayStation redirect app relaunch on cold start)

### Added

- Android: Added `redirectAppRelaunchEnabled` setting to `AdvancedSettingsAndroid` — when enabled and using an external browser for payments, tapping "Back to Game" in PayStation relaunches the app if it was killed during the payment flow
- Android: Added post-merge manifest fix to ensure `PaymentRedirectActivity` intent filter is correctly applied regardless of manifest merge order
- Android: Added discount percentage to product data
- Android: Added developer payload to purchase response data

### Fixed

- WebGL: Fixed cancel handler delegate leak in PayStation order tracking
- Android: Fixed inverted purchase state logic — purchased items were reported as pending
- Android: Fixed NPE when PayStation UI settings are not configured
- Android: Fixed invalid JSON in purchase restore bypass path
- Android: Fixed floating-point precision loss in product price serialization
- Standalone: Fixed malformed URL when using custom PayStation domain

## [3.1.4] - 2026-17-03

### Changed

- iOS: replaced bundled framework with Swift Package Manager dependency resolution
- Android: updated SDK to `3.0.37` (cancellation reason querying, invoice status check fix)

### Added

- Android: Added `queryCancellationReasonEnabled` setting to `AdvancedSettingsAndroid` to distinguish between user cancellations and failed payments

### Fixed

- Android: Fixed `emailCollectionConsentOptInEnabled` not being passed

## [3.1.3] - 2026-13-03

### Changed

- Android: updated SDK to `3.0.36` (various bugfixes)

## [3.1.2] - 2026-03-09

### Changed

- Minimum Unity version raised to 2022.3 LTS

## [3.1.1] - 2026-02-04

### Changed

- Android: updated SDK to `3.0.35` (incompatible browser list update, etc).

## [3.1.0] - 2026-01-22

### Fixed

- **BREAKING CHANGE:** Fixed incorrect `XsollaStoreClientProduct.formattedPrice` value when discounts are applied in standalone and web builds. This behavior is now synchronized with mobile platforms. Use `XsollaStoreClientProduct.formattedPriceWithDiscount` to retrieve the discounted price.

### Added

- Added support for retry policies configuration
- Added support for payment flow events
- Added support for programmatic purchase cancellation

### Changed

- iOS: updated SDK to `3.8.0`
- Android: updated SDK to `3.0.34`

## [3.0.33] - 2026-20-01

### Changed

- Improvements to Android dependency validator (UI, etc)
- Payment flow cancellation is now logged as a warning

## [3.0.32] - 2026-15-01

### Added

- Added support for custom domain overrides

### Changed

- iOS: updated SDK to `3.6.0`
- Android: updated SDK to `3.0.32`

## [3.0.31] - 2025-30-12

### Added

- Android: added support for manually updating the access token

### Changed

- Android: updated SDK to `3.0.30`

## [3.0.30] - 2025-30-12

### Added

- Android: added support for `VisibleLogo`

### Changed

- iOS: updated SDK to `3.5.0`
- Android: updated SDK to `3.0.29` (webview, custom tabs fixes, etc)

## [3.0.29] - 2025-15-12

### Added

- iOS: support for email consent opt-in.

### Changed

- iOS: updated SDK to `3.4.1`

## [3.0.28] - 2025-11-12

### Added

- Android: support for email consent opt-in.
- Android: browser provider filtering and prioritization.

### Fixed

- iOS: Fixed an issue where the redirect delay was not passed correctly.
- iOS: Fixed handling of the web view orientation lock option and the local purchase restoration toggle.

### Changed

- Android: updated SDK to `3.0.27`

## [3.0.27] - 2025-09-12

### Fixed

- iOS: corrected the raw numeric price value.

### Changed

- iOS: updated SDK to `3.3.2`

## [3.0.26] - 2025-02-12

### Changed

- iOS: updated SDK to `3.3.1`

## [3.0.25] - 2025-02-12

### Changed

- Android: updated SDK to `3.0.26` (`sdk` in payment tokens)

### Fixed

- Android: redirect URL wasn't always passed from Unity to SDK

## [3.0.24] - 2025-28-11

### Changed

- iOS: updated SDK to `3.3.0`
- Android: updated SDK to `3.0.25` (redirect URL fixes)

## [3.0.23] - 2025-26-11

### Changed

- Android: Updated SDK to `3.0.24` (improved logging)

### Fixed

- Android: A crash related to using functionality unavailable in Java < 21
- Android: Fixed transaction ID for completed purchases
- Correct price values for queried products

## [3.0.22] - 2025-21-11

### Changed

- Android: Updated SDK to `3.0.23` (support for `install_source` parameter in payment tokens)

## [3.0.21] - 2025-20-11

### Changed

- Android: Updated SDK to `3.0.22` (payment flow status tracking improvements)

## [3.0.20] - 2025-17-11

### Added

- Android: Added support for token-based billing flows that don't have a valid order ID attached to them

### Changed

- Android: Updated SDK to `3.0.21`

## [3.0.19] - 2025-14-11

### Changed

- iOS: updated SDK to `3.2.0`
- Android: updated SDK to `3.0.20` (locale override, logger fixes and improved invoice status querying)

### Fixed

- Fixed the OAuth2 client ID parsing (supports positive numbers only)

## [3.0.18] - 2025-07-11

### Changed

- Android: updated SDK to `3.0.18` (WebShop URL parameter fixes)

## [3.0.17] - 2025-06-11

### Changed

- Android: updated SDK to `3.0.17`

## [3.0.16] - 2025-03-11

### Changed

- iOS: updated SDK to `3.1.2`

## [3.0.15] - 2025-24-10

### Changed

- iOS: updated SDK to `3.1.1`

## [3.0.14] - 2025-24-10

### Fixed

- Cleanup logs

## [3.0.13] - 2025-23-10

### Added

- Android dependency validator tool under `Window -> Xsolla -> SDK -> Dev Tools`

### Fixed

- Exception when running on the start thread without initialization.
- Order tracking now properly finishes instead of endlessly polling.

### Changed

- Android: updated SDK to `3.0.16`
- iOS: updated SDK to `3.1.0`

## [3.0.12] - 2025-15-10

### Added

- Support for non-personalized catalog fetches (Android only)

### Changed

- Android: updated SDK to `3.0.14`

## [3.0.11] - 2025-07-10

### Added

- Android: support for Buy Button and Web Shop link-outs

### Fixed

- iOS: fixed validation of restored transactions

### Changed

- iOS: updated SDK to `3.0.5`
- Android: updated SDK to `3.0.13`

## [3.0.10] - 2025-12-09

### Fixed

- Standalone: fixed a null exception when tracking a payment status

## [3.0.9] - 2025-10-09

### Changed

- Android: updated SDK to `3.0.9`

### Added

- Standalone: payment cancellation event handling

## [3.0.8] - 2025-09-09

### Changed

- Android: updated SDK to `3.0.8`

### Fixed

- iOS: receipt won't be malformed.

## [3.0.7] - 2025-05-09

### Changed

- `XsollaPurchasingStore` will correctly return `PurchaseFailureReason.UserCancelled` when the purchase was cancelled.
- When purchase fails `XsollaStoreClient` will provide `XsollaStoreClientError` which now has `XsollaStoreClientPurchaseErrorCode` containing error's code. 
- iOS: updated SDK to `3.0.3`
- Android: updated the bridge to support the revamped error handling

## [3.0.6] - 2025-03-09

### Changed

- iOS: updated SDK to `3.0.2`

### Fixed

- iOS: missing callbacks for payments initiated by PayStation token.

## [3.0.5] - 2025-01-09

### Changed

- Android: updated SDK to `3.0.7`

## [3.0.4] - 2025-29-08

### Added

- Android: added helper utilities to `XsollaStoreClientInfo`:
	- `IsGooglePlayStoreInstalled()`
	- `IsInstalledFromGooglePlayStore()`
	- `QueryGooglePlayCountryCodeAsync()`

### Fixed

- Web: fixed a compilation issue under certain conditions
- Fixed a bug where PayStation would open multiple times on purchase with a token

## [3.0.3] - 2025-25-08

### Changed

- Android: updated SDK to `3.0.4`

## [3.0.2] - 2025-18-08

### Added

- Android: support for `SimpleMode` configurations

### Fixed

- Android: fixed `localPurchasesRestore` parsing issue

## [3.0.1] - 2025-15-08

### Changed

- Updated iOS SDK to `3.0.1`
- Upgraded `Unity In-App Purchasing` dependency to `4.13.0` to comply with Google requirements (effective August 31, 2025)

## [3.0.0] - 2025-11-08

### Changed

- Updated Android library dependency to `3.0.0`

### Added

- Added utilities for social access token login

## [2.0.22] - 2025-05-08

### Changed

- Updated Android library dependency to `2.1.26-alpha`

## [2.0.21] - 2025-16-07

### Changed

- Updated Android library dependency to `2.1.22-alpha`

## [2.0.20] - 2025-15-07

### Changed

- Updated Android library dependency to `2.1.21-alpha`

## [2.0.19] - 2025-14-07

### Fixed

- Fixed receipt parsing, when trying to validate a purchase using store extensions

## [2.0.18] - 2025-20-06

### Changed

- Updated Android SDK to `2.1.20-alpha`

## [2.0.17] - 2025-12-06

### Changed

- Updated Android SDK to `2.1.18-alpha`

## [2.0.16] - 2025-03-06

### Changed

- Updated Android SDK to `2.1.15-alpha`

## [2.0.15] - 2025-27-05

### Changed

- Updated iOS SDK to `2.2.1`

## [2.0.14] - 2025-10-04

### Changed

- Android
	- Updated dependencies:
		- Mobile SDK: `2.1.12-alpha` -> `2.1.14-alpha`

## [2.0.12] - 2025-27-02

### Added

- Android: Improved custom tabs detection logic

### Changed

- Android
	- Updated dependencies:
		- Mobile SDK: `2.1.11-alpha` -> `2.1.12-alpha`

## [2.0.11] - 2025-26-02

### Changed

- Android
	- Updated dependencies:
		- Mobile SDK: `2.1.10-alpha` -> `2.1.11-alpha`

- iOS
	- Removed Pods dependencies

## [2.0.10] - 2025-18-02

### Changed

- Android
	- Updated dependencies:
		- Mobile SDK: `2.1.9-alpha` -> `2.1.10-alpha`
	- Added a changelog for the Mobile SDK dependency at `Plugins\XsollaMobileSDK\Android`

## [2.0.9] - 2025-12-02

### Changed

- iOS
	- Updated dependencies:
		- Mobile SDK: `2.0.2` -> `2.0.9`

## [2.0.8] - 2025-12-02

### Changed

- Android
	- Updated dependencies:
		- Mobile SDK: `2.1.8-alpha` -> `2.1.9-alpha`

## [2.0.7] - 2025-10-02

### Changed

- Android
	- Updated dependencies:
		- Mobile SDK: `2.1.7-alpha` -> `2.1.8-alpha`

## [2.0.6] - 2025-05-02

### Fixed

- Android
	- Fixed a potential crash on initialization

## [2.0.5] - 2025-04-02

### Changed

- Android
	- Updated dependencies:
		- Mobile SDK: `2.1.6-alpha` -> `2.1.7-alpha`
		- Store SDK: `2.5.10` -> `2.5.11`
		- Login SDK: `6.0.11` -> `6.0.14`
		- Payments SDK: `1.4.4` -> `1.4.5`
	
### Added

- Android
	- Introduced ProGuard rules for obfuscation and shrinking

## [2.0.4] - 2024-12-02

### Changed

- Android
	- Updated Mobile SDK `2.1.5-alpha` -> `2.1.6-alpha`
	- Updated Payments SDK dependency `1.4.3` -> `1.4.4`
- iOS
	- Updated Mobile SDK `1.0.1` -> `1.0.2`

### Added

- Added support for external browser when opening the payment view (iOS/Android)

## [2.0.3] - 2024-10-08

### Added

- Added developerPayload support

## [2.0.2] - 2024-09-11

### Added

- Implemented extension method `GetPayload` for `PurchaseEventArgs`

### Changed

- Upgraded Mobile SDK Android dependency `2.0.2` -> `2.1.0-alpha`

## [2.0.1] - 2024-09-04

### Changed

- Upgraded Mobile SDK Android dependency `2.0.0` -> `2.0.2`

## [2.0.0] - 2024-08-15

### Changed

- Initial package version
