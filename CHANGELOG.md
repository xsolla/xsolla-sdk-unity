## [3.1.25] - 2026-11-27

### Added

- Android: the SDK creates `Assets/Plugins/Android` when Android is the active build target (the External Dependency Manager fails to resolve in a new project without it)

### Changed

- `XsollaStoreClient.Initialize` no longer throws when the store client throws before answering (the exception is logged and the completion handler gets an `initialization failed: ` error)
- `XsollaPurchasingModule` is now left out of the build on `Unity In-App Purchasing` older than 4.13.0 (the editor logs a warning); upgrade to 4.15.1
- Win/Mac/WebGL: a failed purchase's `XsollaStoreClientError` now carries the API response's `statusCode`, `errorCode` and `errorMessage`, as on Android
- Android: purchase failures raised before the payment flow opens now name the product (`sku=<id>`)

### Fixed

- Win/Mac/WebGL/Android: a purchase still open at `Deinitialize` or a repeated `Initialize` now fails with `Aborted` (a purchase paid meanwhile arrives with the next restore)
- Win/Mac/WebGL: `XsollaStoreClient.Initialize` called again without `Deinitialize` now signs the user in and fetches the products again (the fetch returned no products and restores failed with `InvalidToken`)
- Win/Mac/WebGL: with the Events API, purchases and restore polling after a repeated `Initialize` now use the latest session
- `XsollaStoreClient.Initialize` now answers once when the configuration can't be resolved (a failed delayed configuration task or a mapper that throws or returns null) or the store client throws
- `XsollaLoginClient` sign-in, `RefreshToken` and `ClearToken` now fail with a `configuration failed: ` error when the delayed configuration task or its mapper fails (they never answered)
- Android: `XsollaStoreClient.Deinitialize` now always calls its completion handler, also before the store connects or when called twice, and accepts a null one
- Android: `XsollaStoreClient.Initialize` called again while an earlier call is still connecting now answers only the latest call
- Android: a purchase event or store connection result that arrives after `Deinitialize` no longer crashes the app
- Android: when the native bridge can't be created or a call can't build its arguments (such as bridge callbacks stripped from the build), store and login calls now fail through their error callbacks instead of throwing
- Android: when the native SDK fails to start, product requests and restores fail with the error and `XsollaPurchasingModule` initialization fails as with no products available
- `XsollaPurchasingModule` no longer leaves Unity IAP initialization waiting forever when the delayed configuration task fails or the store client throws while starting or fetching products (initialization fails instead)
- `XsollaPurchasingModule` no longer holds back every purchase for the session when the first product fetch or the app's `IStoreListener` callbacks throw during initialization
- An exception thrown by an SDK callback, such as an app's Unity IAP `ProcessPurchase`, no longer drops the other callbacks due in the same frame or waiting on the same result (it is still reported to the Unity console)
- Unity IAP: a purchase reported twice before it reached Unity IAP at startup now consumes the quantity handed to Unity IAP
- Unity IAP: a consumable delivered before `UpdateAccessToken` switched users is no longer consumed under the new user (it is consumed once that user's token is set again)
- Win/Mac/WebGL: with the Events API, a purchase in WebShop mode or with a payment token now completes when its payment arrives (it failed at the 10-minute tracking limit)
- Win/Mac/WebGL: with the Events API and no `localPurchasesRestoreInterval`, each purchase now fails 10 minutes after it started (a new purchase restarted the limit of those already tracked)
- Win/Mac/WebGL: with the Events API, a paid payment-token purchase with an invalid order ID now completes at once when `allowTokenOnlyFinishedStatusWithoutOrderId` is set, and fails with its error otherwise
- Win/Mac/WebGL: without the Events API, a purchase refunded before delivery (order status `canceled`) now fails (Unity IAP: `PaymentDeclined`)
- Win/Mac/WebGL: without the Events API, a temporary order status failure (network error, server error, rate limit) no longer fails the purchase; tracking continues up to the 10-minute limit
- WebGL: without the Events API, a payment-token purchase in the in-page Pay Station widget now gets its result outside Safari (tracked while the widget is open and up to 10 minutes after it closes)
- Win/Mac/WebGL: `XsollaStoreClient.PurchaseProduct` no longer answers twice when a purchase completes after it was reported failed, such as at the 10-minute tracking limit (it arrives through `OnRestore`; Unity IAP is unaffected)
- Win/Mac/WebGL: buying an item again while its earlier purchase in WebShop mode or with the same payment token is tracked now fails the earlier purchase and tracks the new one (the new one never got a result)
- Win/Mac/WebGL: a purchase started again from an earlier purchase's result, such as a retry from its failure handler or during `StopAllOrdersFromTracking`, now gets its own result
- Win/Mac/WebGL: a failed purchase keeps its error code in every flow (on WebGL, closing the widget of a payment-token purchase is reported as cancelled, not declined)
- Win/Mac/WebGL: a purchase in WebShop mode with no web shop URL or user ID now fails with a plain message, without the `Error: UnknownError. Message: ` prefix
- Win/Mac/WebGL: error messages no longer end in a doubled period (the launcher sign-in error now reads as a message, not a status code)
- Android: when several purchases are open at once, such as on a double tap, each now gets a single result except one in WebShop mode without a payment token (closing the second payment view reports `UserCancelled`)
- Android: a purchase that fails to start no longer also receives the next purchase's result
- Android: a purchase in WebShop mode no longer also receives the result of a purchase started after it (a paid one arrives with the next restore)
- Android: a purchase the native SDK refuses to start now fails (Unity IAP: `UserCancelled` when cancelled by `StopAllOrdersFromTracking`, otherwise `PaymentDeclined`)
- Android: a purchase of a product the store returns no details for now fails (Unity IAP: `PaymentDeclined`)
- Android: a failed purchase whose message contains quotes or backslashes keeps its error code (a cancellation was reported as a declined payment)
- Android: `StopAllOrdersFromTracking` now cancels every open purchase except one in WebShop mode without a payment token (it cancelled only the latest)
- Android: a payment-token purchase now starts its payment flow on the UI thread, as the native SDK requires
- Android: a restore after switching back to a user in the same session no longer delivers that user's unconsumed items again under new transaction ids
- Android: in JWT mode, `XsollaStoreClient.GetAccessToken` now answers the token in use when it was called, even if `UpdateAccessToken` follows at once
- Android: social and widget login and `ClearToken` now report a failure to convert their result through the error callback (an error text containing `%` left the request unanswered)
- iOS: a purchase restore requested before the native sign-in completes now answers with nothing restored instead of failing as not configured

## [3.1.24] - 2026-11-20

### Added

- Win/Mac/WebGL/Android: `XsollaClientConfiguration.secretsObfuscated` and `Builder.SetSecretsObfuscated(bool)` to obfuscate tokens, purchase receipts and other secrets in SDK logs (off by default; on by default from 3.2.0)
- Win/Mac/WebGL/Android/iOS: `XsollaStoreClientPurchasedProduct.origin` tells a live purchase from one restored through the Events API or the inventory (also in the Unity IAP receipt)

### Changed

- Android: updated SDK to `3.1.4` (sign-in runs on the SDK's own login code instead of the Xsolla Login SDK, which is no longer a dependency; users stay signed in across the upgrade; device ID sign-in restores its saved session; the widget opens in any Custom Tabs browser and fails after 15 minutes without a redirect; silent sign-in renews an expired session, so it can now fail offline; purchases, consumes, validation and queries renew a rejected session and retry once; a consume is no longer resent after a server error once sent; the first payment web view of a session no longer stays blank; a saved sign-in that Android backup restores onto a new install is discarded)
- Android: the native SDK, sign-in included, now follows `logLevel` and `secretsObfuscated` (`Warning` now also shows its warnings)
- iOS: with `secretsObfuscated` on, the iOS SDK's warnings and errors in the Unity log obfuscate their token values
- Win/Mac/WebGL: OAuth 2.0 sign-in with a social access token or device ID now uses PKCE, and every OAuth 2.0 sign-in sends a random `state` instead of `xsollatest`
- Win/Mac: Xsolla Login widget sign-in refuses a redirect whose `state` differs from the one it sent
- Win/Mac/WebGL: `ClearToken` now keeps a device ID session and revokes any other session's refresh token on the server (a failed server revoke only logs a warning)

### Fixed

- Android: the browser-provider configuration warning and the store's sign-in setup lines (`Authenticating using ...`) now follow `logLevel` instead of logging even at `None`
- Android: `ClearToken` now signs the user out of the Xsolla Login widget session, keeping a device ID session, and revokes its refresh token (a failed server revoke only logs a warning, and a store keeps the old access token until you deinitialize it or call `UpdateAccessToken`)
- Android: `XsollaLoginClient.LoginWithSocialAccount` now returns the access token, refresh token and expiry instead of the bridge's raw JSON as the access token
- Win/Mac: Xsolla Login widget sign-in now reports an error redirect (e.g. a cancelled social sign-in) through its error callback instead of never answering
- Win/Mac/WebGL: a store with a redirect URL set no longer fails with `010-023` when it renews a sign-in made with a social access token
- Win/Mac/WebGL: the login client's `Debug` and `Warning` log levels now also show the SDK's network and sign-in messages, as the store client's do
- Win/Mac: starting a new Xsolla Login widget sign-in cancels one still waiting (its `onCancel` fires), so an abandoned sign-in no longer answers for the next one
- Android: a purchase that arrives with no purchase request waiting for it can now be consumed (its consume failed as an unknown purchase, so the next restore granted the item again)
- Win/Mac/WebGL: a consume with the Events API now answers once (it answered twice when the purchase event had a sub-event)
- Win/Mac/WebGL: a product fetch with `fetchProductsWithGeoLocale` no longer hangs when the runtime has no culture for the user's geo (prices then use the configured locale)
- Win/Mac/WebGL: an Events API request refreshes a rejected token at most once (a token the Events API kept rejecting was refreshed without limit)
- Android: units restored from the inventory now get per-session transaction ids instead of reusing one, so Unity IAP 4.x no longer consumes a unit without granting it (their receipts now carry order id `0`)
- Android: consuming part of a restored purchase now consumes only the units asked for (it consumed the whole purchase)
- Android: a consume whose outcome is unknown is now checked against the user's inventory instead of being left to a retry that could consume another owned unit (one it cannot settle is refused until the next launch)
- WebGL: the Pay Station widget no longer throws a browser error when it closes, which left the page unscrollable
- WebGL: a purchase reuses the Pay Station widget script the page already loaded instead of adding it to the page again
- Android: a non-consumable bought through Unity IAP 4.x is no longer granted again on the next launch (its restores now carry the purchase's transaction id)
- Win/Mac/WebGL: a store given the saved session's own access token, through `UpdateAccessToken` or its configuration, now keeps that session (it dropped the session's refresh token, so `ClearToken` then discarded a device ID session)

## [3.1.23] - 2026-11-04

### Changed

- Android: updated SDK to `3.0.62` (widget sign-in with a locale set now opens the widget in that locale instead of failing to open)
- README: documented the redirect URIs to register on the OAuth 2.0 client for each Android sign-in method

### Fixed

- Android: builds with minification enabled no longer strip the SDK's Java bridge, which left the SDK unable to initialize; the plugin now ships its own keep rules, so a `-keep` line in your `proguard-user.txt` is no longer needed

## [3.1.22] - 2026-11-02

### Added

- Android, iOS: `XsollaClientConfiguration.installSource` and `Builder.SetInstallSource(string)` to report your own distribution channel, such as your website or launcher, instead of the detected one

### Changed

- Android: updated SDK to `3.0.61` (install source; a restored purchase's order ID is now scoped to the project and the user, so the IDs of units still held when updating change once and are reported to Unity IAP as new transactions; `validateAsync` on a restored purchase reports OK; billing flow failures name the SKU in their message (`sku=<id>`); billing flows, product queries, order validation and events requests that never completed on an unexpected failure now report it; up to 32 payment views can wait to open instead of 4; social sign-in rejects a blank authorization code)
- iOS: updated SDK to `3.14.0`
- Upgraded `Unity In-App Purchasing` dependency to `4.15.1`: it ships Google Play Billing Library 8, which Google Play requires for new apps and updates (effective August 31, 2026). Also picks up the macOS and visionOS fixes from 4.14.2 and 4.15.1

### Fixed

- `XsollaPurchasingModule` hands each purchase to Unity IAP at most once per session, so the same transaction is no longer reported to Unity IAP again when the native SDK re-delivers an owned, unconsumed item, a restore races a purchase, or `RestoreTransactions` reports a purchase already restored at initialization

## [3.1.21] - 2026-09-29

### Added

- `XsollaClientConfiguration.attributionUserId` and `Builder.SetAttributionUserId(string)`: sent as `custom_parameters.custom_id`, so purchase events reach attribution platforms (AppsFlyer, etc.) keyed on your attribution ID. It must equal the customer user ID your attribution SDK reports

### Changed

- Android: updated SDK to `3.0.58` (attribution user ID, argument-less `TokenValue.getExpiryTime()`, partner custom parameters on orders)
- iOS: updated SDK to `3.13.0` (attribution user ID in `SKPaymentSettings`)
- Clarified `userId` / `SetUserId` docs: sent as `custom_user_id`, not the attribution ID

### Fixed

- Android: a login token without an explicit expiry now takes its expiry from the `exp` claim instead of the login time

## [3.1.20] - 2026-09-28

### Added

- iOS: native SDK warnings and errors are forwarded to the Unity log via `XsollaLogger`
- iOS: `XsollaStoreClientError` carries `statusCode`, `errorCode` and `errorMessage` from the Xsolla response (empty for non-Xsolla failures)

### Changed

- iOS: updated SDK to `3.12.0`
- Android: updated SDK to `3.0.57` (in-session purchases no longer reported as restored under the Events API strategy; payment-token purchases with order IDs above the `int` range no longer fail or report another order's item)
- Order IDs are now `long` across purchases, receipts, and order status/tracking APIs; Xsolla order IDs are 64-bit
- iOS: failed purchases and catalog fetches report `Internal`, or `Unauthorized` for a rejected login token, with the same detail, instead of `Unknown`

### Fixed

- `IPurchaseReceipt.purchaseDate` reports the payment time where the platform knows it; undatable restores (e.g. from inventory) report their arrival time
- Android: `quantity` reports the units a purchase covers instead of always `1`; iOS reports it on restores only
- iOS/Android: a malformed or oversized `orderId` no longer throws during purchase parsing
- Win/Mac: orders tracked from a payment token keep their full 64-bit ID; IDs past the `int` range used to wrap and poll the wrong order

## [3.1.19] - 2026-09-11

### Changed

- Android: updated SDK to `3.0.55` (payment and login flow queueing, payment redirect handling, surviving config changes and split-screen resize, stable order IDs on restore, auth crash fixes)

### Fixed

- All platforms: a restored purchase is now consumed only once; Unity IAP re-finishes an already-finished transaction on every catalog refresh, which failed with a consume error on Android and could drain an extra inventory unit elsewhere
- Win/Mac: restored purchases from the Events API now carry `invoiceId`, matching the normal purchase completion path and the Android/iOS behavior
- Win/Mac/WebGL: order status polling, the cached-order success callback, the create-order cooldown, and catalog fetch retries now use unscaled time; previously they stalled while the game set `Time.timeScale` to `0`

## [3.1.18] - 2026-08-19

### Added

- Added `XsollaStoreClientPurchaseArgs.Builder.SetExternalTransactionToken(string)` for passing external transaction token on supported platforms
- Android: Added support for `XsollaStoreClientPurchaseArgs.Builder.SetExternalTransactionToken(string)` setter

### Changed

- Android: updated SDK to `3.0.52`
- iOS: updated SDK to `3.10.0`

### Fixed

- Win/Mac: `CXsollaStoreClientPurchaseArgs::SetExternalId` is now forwarded to the managed layer

## [3.1.17] - 2026-06-30

### Fixed

- Win/Mac/WebGL: login widget close event no longer fires before the token is received

## [3.1.16] - 2026-06-18

### Added

- Win/Mac/WebGL/Android: `collapseRestoredMultiUnitPurchases` on `XsollaClientSettings` — collapse restored multi-unit SKUs into one quantity notification (`true`) or one per unit (`false`, default)
- Android: deprecated `AdvancedSettingsAndroid.SetCollapseRestoredMultiUnitPurchases` for `XsollaClientSettings.Builder.SetCollapseRestoredMultiUnitPurchases`; both are honored
- Win/Mac/WebGL/Android: `XsollaStoreClientExtensionsSettings.Builder.SetProductFetchSettings(...)` — per-SKU fetch tunables `maxItemsPerRequest`, `maxParallelRequests`, `cacheTtlMillis` (defaults 50 / 4 / 1h); `cacheTtlMillis` is ignored on standalone (no cache)
- Win/Mac/WebGL/Android: `XsollaClientConfiguration.Builder.SetSkipMissingProductsOnFetch(bool)` — when `true` (default), skips backend-reported non-existent SKUs and fails only if all are missing

### Changed

- Android: updated SDK to `3.0.46` (per-SKU product fetch)
- Win/Mac/WebGL: per-SKU fetch retries transient failures (transport, 408/429/5xx) per the `QueryProducts` retry profile; a slow or failing batch no longer blocks sibling batches

### Fixed

- Win/Mac/WebGL: restore now consumes all owned units of a multi-unit SKU; previously one per launch without the Events API
- `XsollaStoreClientPurchaseArgs.developerPayload` is now applied per purchase; previously the per-call override was ignored for the global payload
- Win/Mac/WebGL: a failed fetch no longer marks its SKUs as loaded (recorded only on success); the dedup cache is cleared on `Deinitialize` so a re-initialized client refetches

## [3.1.15] - 2026-05-15

### Fixed

- iOS: `SetRedirectUrl()` is now respected by the WebShop flow.

## [3.1.14] - 2026-05-05

### Added

- Android: support for AGP 9.x (Unity 6.4+)

## [3.1.13] - 2026-05-05

### Changed

- Android: updated SDK to `3.0.43` (improved auth token refreshing)

### Fixed

- Android: correct dependency URL

## [3.1.12] - 2026-29-04

### Changed

- Win/Mac/Web: improved auth token refreshing (takes social claims into account)

## [3.1.11] - 2026-23-04

### Changed

- Android: updated SDK to `3.0.42` (a fix for transient dependency conflicts)

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
