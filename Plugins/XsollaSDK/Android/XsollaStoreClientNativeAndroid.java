package com.xsolla.mobile.unity;

import android.app.Activity;
import android.content.Context;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import android.text.TextUtils;
import android.util.Log;
import android.util.Pair;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.core.os.HandlerCompat;

import com.xsolla.android.mobile.*;
import com.xsolla.android.mobile.Error;
import com.xsolla.android.mobile.LocaleInfo;
import com.xsolla.android.mobile.LogLevel;
import com.xsolla.android.mobile.JWT;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.Date;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.Consumer;
import java.util.function.Function;
import java.util.stream.Collectors;

/** @noinspection unused*/
public final class XsollaStoreClientNativeAndroid implements PurchasesUpdatedListener {
    private static final String TAG = "XsollaStoreClientNativeAndroid";

    public static final class XsollaUnityBridgeJsonCallbackUtil {
        @NonNull
        public static final String EMPTY_RESULT = "{}";
    }

    public interface IXsollaUnityBridgeJsonCallback {
        void onSuccess(@NonNull final String result);
        void onError(@NonNull final String error);
    }

    /**
     * A holder for a {@link BillingClient} that provides thread-safe
     * management methods.
     */
    private static final class AsyncBillingClientHolderSafe {
        @Nullable
        private BillingClient billingClient = null;

        public boolean shutdown() {
            final BillingClient billingClient_;

            synchronized (this) {
                billingClient_ = billingClient;
                billingClient = null;
            }

            if (billingClient_ == null || !billingClient_.isReady()) {
                return false;
            }

            billingClient_.endConnection();

            return true;
        }

        public void set(@Nullable final BillingClient billingClient) {
            synchronized (this) {
                this.billingClient = billingClient;
            }
        }

        @Nullable
        public BillingClient get() {
            synchronized (this) {
                return billingClient;
            }
        }

        @NonNull
        public BillingClient getOrThrowIfNotReady() {
            final BillingClient billingClient_;
            synchronized (this) {
                billingClient_ = billingClient;
            }
            if (billingClient_ == null || !billingClient_.isReady()) {
                throw new IllegalStateException(
                    "The billing client is not available or hasn't been initialized"
                );
            }
            return billingClient_;
        }
    }

    static abstract class LoginResult {
        public static final class Success extends LoginResult {
            @NonNull
            public final String accessToken;

            @Nullable
            public final String refreshToken;

            @NonNull
            public final Date accessTokenExpiryTime;

            public Success(
                @NonNull final String accessToken,
                @Nullable final String refreshToken,
                @NonNull final Date accessTokenExpiryTime
            ) {
                this.accessToken = accessToken;
                this.refreshToken = refreshToken;
                this.accessTokenExpiryTime = accessTokenExpiryTime;
            }

            @NonNull
            public JSONObject toJson(boolean useRelativeExpirationTime) throws JSONException {
                final var json = new JSONObject();
                json.put("accessToken", accessToken);
                json.put("refreshToken",
                    !TextUtils.isEmpty(refreshToken) ? refreshToken : ""
                );
                if (useRelativeExpirationTime) {
                    json.put("expiresIn",
                        (accessTokenExpiryTime.getTime() - System.currentTimeMillis()) / 1000
                    );
                } else {
                    json.put("expirationDate", accessTokenExpiryTime.getTime() / 1000);
                }
                return json;
            }

            @Nullable
            public static Success parseJson(
                @NonNull final JSONObject json, boolean preferRelativeExpiration
            ) {
                try {
                    final var expiresInSecs = preferRelativeExpiration
                        ? json.optLong("expiresIn", -1)
                        : -1;

                    final var expirationTimestampMillis = expiresInSecs >= 0
                        ? System.currentTimeMillis() + expiresInSecs * 1000
                        : json.getLong("expirationDate") * 1000;

                    return new Success(
                        json.getString("accessToken"),
                        json.getString("refreshToken"),
                        new Date(expirationTimestampMillis)
                    );
                } catch (Exception e) {
                    logError("Failed to parse LoginResult.Success JSON object: " + e);
                    return null;
                }
            }

            @Nullable
            public static Success parseJson(
                @Nullable final String jsonStr, boolean preferRelativeExpiration
            ) {
                final var maybeJsonStr = Optional.ofNullable(jsonStr).filter(s -> !TextUtils.isEmpty(s));
                final var maybeJson = maybeJsonStr.flatMap(JsonHelper::parse);
                return maybeJson.map(json -> parseJson(json, preferRelativeExpiration)).orElse(null);
            }
        }

        public static final class Failure extends LoginResult {
            @NonNull
            public final String msg;

            public Failure(@NonNull final String msg) {
                this.msg = msg;
            }
        }

        public void voidFold(
            @NonNull final Consumer<Success> onSuccess,
            @NonNull final Consumer<Failure> onFailure
        ) {
            if (this instanceof Success) {
                onSuccess.accept((Success) this);
            } else {
                assert this instanceof Failure;
                onFailure.accept((Failure) this);
            }
        }
    }

    @NonNull
    private final Activity activity;

    @NonNull
    private final AsyncBillingClientHolderSafe billingClientHolder = new AsyncBillingClientHolderSafe();

    @NonNull
    private final Handler mainThreadHandler = HandlerCompat.createAsync(Looper.getMainLooper());

    private IXsollaUnityBridgeJsonCallback m_stateCallback;
    private IXsollaUnityBridgeJsonCallback m_purchaseCallback;

    @NonNull
    private static final AtomicReference<JWTRefresher> jwtRefresher = new AtomicReference<>(null);

    enum SimpleMode {
        Off,
        ServerTokens,
        WebShop
    }

    private static boolean logsDebugEnabled = false;
    private static boolean logsErrorEnabled = false;
    private static boolean useRestore = true;

    @NonNull
    private static SimpleMode simpleMode = SimpleMode.Off;

    private static boolean useBuyButtonSolution = false;

    private static boolean emailCollectionConsentOptIn = false;

    private static boolean queryCancellationReasonEnabled = false;

    private static boolean redirectAppRelaunchEnabled = false;

    private static boolean collapseRestoredMultiUnitPurchases = false;

    @Nullable
    private static String webshopUrl = null;

    @Nullable
    private static String userId = null;

    @Nullable
    private static String redirectUrl = null;

    private static boolean fetchPersonalizedProductsOnly = true;

    @Nullable
    private static ConfigWithoutIntegration.Payments.DomainOverrideConfig domainOverrideConfig = null;

    @Nullable
    private static String trackingId = null;

    private static boolean fetchProductsWithGeoLocale = false;

    @NonNull
    private static final AtomicBoolean forceNonSilentNextWidgetLogin = new AtomicBoolean(false);

    @NonNull
    private final AtomicReference<BillingFlowCanceller> currentPurchaseCanceller = new AtomicReference<>();

    /** @noinspection unused*/
    public XsollaStoreClientNativeAndroid(@NonNull final Activity activity) {
        this.activity = activity;
    }

    private static void logDebug(String msg) {
        if (logsDebugEnabled)
            Log.d(TAG, msg);
    }

    private static void logError(String msg) {
        if (logsErrorEnabled)
            Log.e(TAG, msg);
    }

    public void Initialize(
        @NonNull final String argsJson,
        @Nullable final String additionalSettingsJson,
        @NonNull final IXsollaUnityBridgeJsonCallback stateCallback,
        @NonNull final IXsollaUnityBridgeJsonCallback purchaseCallback,
        @Nullable final IXsollaUnityBridgeJsonCallback paymentEventCallback
    ) {
        m_stateCallback = stateCallback;
        m_purchaseCallback = purchaseCallback;

        final var maybeJson = JsonHelper.parse(argsJson);
        final var maybeSettingsJson = maybeJson.flatMap(json -> JsonHelper.getChildObject(json, "settings"));
        final var debug = maybeJson.map(JsonHelper::jsonToDebug);
        final var logs = maybeJson.map(JsonHelper::jsonToLog);

        final Optional<JSONObject> maybeAdditionalSettingsJson = Optional.ofNullable(additionalSettingsJson)
            .filter(__ -> !__.isBlank())
            .flatMap(JsonHelper::parse);

        final Optional<RetryPolicies> maybeRetryPolicies =
            maybeAdditionalSettingsJson.flatMap(additionalSettingsJson_ ->
                JsonHelper.getChildObject(additionalSettingsJson_, "retryPolicies")
                    .flatMap(retryPoliciesJson ->
                        Optional.ofNullable(JsonHelper.parseRetryPolicies(retryPoliciesJson))
                    )
            );

        //XsollaLogLevel{ Debug,  Warning,  Error,  None }
        logsDebugEnabled = logs.map( l -> l.equals("Debug")).orElse(false);
        logsErrorEnabled = logs.map( l -> !l.equals("None")).orElse(false);

        useRestore = maybeSettingsJson
            .map(settingsJson -> settingsJson.optBoolean("localPurchasesRestore"))
            .orElse(true);

        simpleMode = maybeJson
            .map(json -> json.optString("simpleMode", ""))
            .filter(str -> !TextUtils.isEmpty(str))
            .flatMap(str -> {
                logDebug("Parsing `SimpleMode` setting value: " + str);

                try {
                    return Optional.of(SimpleMode.valueOf(str));
                } catch (IllegalArgumentException e) {
                    logDebug("Failed to parse `SimpleMode` value: " + e);
                    return Optional.empty();
                }
            })
            .orElse(SimpleMode.Off);

        useBuyButtonSolution = maybeSettingsJson
            .map(settingsJson -> settingsJson.optBoolean("useBuyButtonSolution"))
            .orElse(false);

        emailCollectionConsentOptIn = maybeSettingsJson
            .map(settingsJson -> settingsJson.optBoolean("emailCollectionConsentOptInEnabled"))
            .orElse(false);

        webshopUrl = maybeSettingsJson
            .map(settingsJson -> settingsJson.optString("webShopUrl"))
            .filter(str -> !TextUtils.isEmpty(str))
            .orElse(null);

        userId = maybeJson
            .map(json -> json.optString("userId"))
            .filter(str -> !TextUtils.isEmpty(str))
            .orElse(null);

        redirectUrl = maybeSettingsJson
            .flatMap(settingsJson -> JsonHelper
                .getChildObject(settingsJson, "redirectSettings")
                .map(redirectSettingsJson -> redirectSettingsJson.optString("redirectUrl"))
            )
            .filter(str -> !TextUtils.isEmpty(str))
            .orElse(null);

        fetchPersonalizedProductsOnly = maybeJson
            .map(json -> json.optBoolean("fetchPersonalizedProductsOnly", true))
            .orElse(true);

        domainOverrideConfig = maybeJson
            .map(json -> {
                String prodUrl = json.optString("customPayStationDomainProduction", "");
                if (prodUrl.isBlank()) {
                    prodUrl = null;
                }

                String sandboxUrl = json.optString("customPayStationDomainSandbox", "");
                if (sandboxUrl.isBlank()) {
                    sandboxUrl = null;
                }

                if (prodUrl == null && sandboxUrl == null) return null;

                return ConfigWithoutIntegration.Payments.DomainOverrideConfig.getDefault()
                    .withProductionDomain(prodUrl)
                    .withSandboxDomain(sandboxUrl);
            })
            .filter(Objects::nonNull)
            .orElse(null);

        trackingId = maybeJson
            .map(json -> json.optString("trackingId"))
            .filter(s -> !TextUtils.isEmpty(s.trim()))
            .orElse(null);

        fetchProductsWithGeoLocale = maybeJson
            .map(json -> json.optBoolean("fetchProductsWithGeoLocale", false))
            .orElse(false);

        forceNonSilentNextWidgetLogin.set(false);

        final Optional<JSONObject> maybeAdvancedSettings =
            maybeSettingsJson.flatMap(settingsJson ->
                JsonHelper.getChildObject(settingsJson, "advancedSettingsAndroid")
            );

        final Optional<Set<String>> maybeUserBlacklistedProviders =
            maybeAdvancedSettings.flatMap(advancedSettings ->
                JsonHelper.getStringSet(advancedSettings, "userBlacklistedProviders")
            );

        final Optional<Boolean> maybeInternalProviderBlacklistEnabled =
            maybeAdvancedSettings.flatMap(advancedSettings ->
                JsonHelper.jsonToBoolean(advancedSettings, "internalProviderBlacklistEnabled")
            );

        final Optional<Set<String>> maybeUserBlacklistedTWAProviders =
            maybeAdvancedSettings.flatMap(advancedSettings ->
                JsonHelper.getStringSet(advancedSettings, "userBlacklistedTWAProviders")
            );

        final Optional<Boolean> maybeInternalTWAProviderBlacklistEnabled =
            maybeAdvancedSettings.flatMap(advancedSettings ->
                JsonHelper.jsonToBoolean(advancedSettings, "internalTWAProviderBlacklistEnabled")
            );

        final Optional<Map<String, Float>> maybeProviderPriorityWeights =
            maybeAdvancedSettings.flatMap(advancedSettings ->
                Optional.ofNullable(advancedSettings.optJSONObject("userProviderPriorityWeights"))
                    .flatMap(weightsJson -> {
                        final Map<String, Float> weights = new HashMap<>(weightsJson.length());

                        weightsJson.keys().forEachRemaining(key -> {
                            Float value;

                            try {
                                value = (float) weightsJson.getDouble(key);
                            } catch (Exception e) {
                                value = null;
                            }

                            if (value != null) {
                                weights.put(key, value);
                            }
                        });

                        return weights.isEmpty() ? Optional.empty() : Optional.of(weights);
                    })
            );

        final Optional<Boolean> maybeFallbackToFirstAvailableTWAProvider =
            maybeAdvancedSettings.flatMap(advancedSettings ->
                JsonHelper.jsonToBoolean(advancedSettings, "fallbackToFirstAvailableTWAProvider")
            );

        queryCancellationReasonEnabled = maybeAdvancedSettings
            .map(advancedSettings -> advancedSettings.optBoolean("queryCancellationReasonEnabled"))
            .orElse(false);

        redirectAppRelaunchEnabled = maybeAdvancedSettings
            .map(advancedSettings -> advancedSettings.optBoolean("redirectAppRelaunchEnabled"))
            .orElse(false);

        collapseRestoredMultiUnitPurchases = maybeAdvancedSettings
            .map(advancedSettings -> advancedSettings.optBoolean("collapseRestoredMultiUnitPurchases"))
            .orElse(false);

        logDebug("Initialize: " + argsJson);
        logDebug("SimpleMode: " + simpleMode);
        logDebug("UseBuyButtonSolution: " + useBuyButtonSolution);
        logDebug("LocalPurchasesRestore: " + useRestore);
        logDebug("WebShopUrl: " + (webshopUrl != null ? webshopUrl : "N/A"));
        logDebug("UserId: " + (userId != null ? userId : "N/A"));
        logDebug("RedirectUrl: " + (redirectUrl != null ? redirectUrl : "N/A"));
        logDebug("FetchPersonalizedProductsOnly: " + fetchPersonalizedProductsOnly);
        logDebug("TrackingId: " + (trackingId != null ? trackingId : "N/A"));
        logDebug("FetchProductsWithGeoLocale: " + fetchProductsWithGeoLocale);
        logDebug("QueryCancellationReasonEnabled: " + queryCancellationReasonEnabled);
        logDebug("RedirectAppRelaunchEnabled: " + redirectAppRelaunchEnabled);
        logDebug("CollapseRestoredMultiUnitPurchases: " + collapseRestoredMultiUnitPurchases);
//      logDebug("InvokePurchasesUpdatedIfOrderIdMissing: " + fetchProductsWithGeoLocale);
        logDebug("AdditionalSettings: " + maybeAdditionalSettingsJson.orElse(null));

        // If there's an existing instance for some reason,
        // shut it down gracefully.
        billingClientHolder.shutdown();

        final var config = maybeJson.flatMap(JsonHelper::jsonToConfig);

        final var billingClient = BillingClient
            .newBuilder(activity)
            .setConfig(config
                .map(config_ -> config_
                    .withCommon(common -> common
                        .withDebugEnabled(debug.orElse(false))
                        .withRetryPolicies(maybeRetryPolicies.orElse(null))
                        .withCollapseRestoredMultiUnitPurchases(collapseRestoredMultiUnitPurchases)
                    )
                    .withPayments(payments -> {
                        ConfigWithoutIntegration.Payments.Activity newActivity = payments.getActivity();
                        if (newActivity == null) {
                            newActivity = ActivityUtils.getDefaultActivity();
                        }

                        if (maybeUserBlacklistedProviders.isPresent() ||
                            maybeInternalProviderBlacklistEnabled.isPresent() ||
                            maybeUserBlacklistedTWAProviders.isPresent() ||
                            maybeInternalTWAProviderBlacklistEnabled.isPresent() ||
                            maybeProviderPriorityWeights.isPresent() ||
                            maybeFallbackToFirstAvailableTWAProvider.isPresent()) {
                            if (newActivity.isCustomTabs()) {
                                final ConfigWithoutIntegration.Payments.Activity.CustomTabs customTabs =
                                    (ConfigWithoutIntegration.Payments.Activity.CustomTabs) newActivity;

                                newActivity = customTabs
                                    .withBlacklistedProviders(maybeUserBlacklistedProviders.orElse(null))
                                    .withInternalProviderBlacklistEnabled(
                                        maybeInternalProviderBlacklistEnabled.orElse(
                                            customTabs.isInternalProviderBlacklistEnabled()
                                        )
                                    )
                                    .withBlacklistedTWAProviders(maybeUserBlacklistedTWAProviders.orElse(null))
                                    .withInternalTWAProviderBlacklistEnabled(
                                        maybeInternalTWAProviderBlacklistEnabled.orElse(
                                            customTabs.isInternalTWAProviderBlacklistEnabled()
                                        )
                                    )
                                    .withProviderWeights(maybeProviderPriorityWeights.orElse(null))
                                    .withFallbackToFirstAvailableTWAProvider(
                                        maybeFallbackToFirstAvailableTWAProvider.orElse(
                                            customTabs.isFallbackToFirstAvailableTWAProvider()
                                        )
                                    );
                            } else if (newActivity.isTrustedWebActivity()) {
                                final ConfigWithoutIntegration.Payments.Activity.TrustedWebActivity trustedWebActivity =
                                    (ConfigWithoutIntegration.Payments.Activity.TrustedWebActivity) newActivity;

                                newActivity = trustedWebActivity
                                    .withBlacklistedProviders(maybeUserBlacklistedProviders.orElse(null))
                                    .withInternalProviderBlacklistEnabled(
                                        maybeInternalProviderBlacklistEnabled.orElse(
                                            trustedWebActivity.isInternalProviderBlacklistEnabled()
                                        )
                                    )
                                    .withBlacklistedTWAProviders(maybeUserBlacklistedTWAProviders.orElse(null))
                                    .withInternalTWAProviderBlacklistEnabled(
                                        maybeInternalTWAProviderBlacklistEnabled.orElse(
                                            trustedWebActivity.isInternalTWAProviderBlacklistEnabled()
                                        )
                                    )
                                    .withProviderWeights(maybeProviderPriorityWeights.orElse(null))
                                    .withFallbackToFirstAvailableTWAProvider(
                                        maybeFallbackToFirstAvailableTWAProvider.orElse(
                                            trustedWebActivity.isFallbackToFirstAvailableTWAProvider()
                                        )
                                    );
                            } else {
                                Log.w(TAG, "Browser provider configuration has been specified, but the activity type " +
                                    "isn't either `WebViewType.System` or `WebViewType.Trusted` (current=" +
                                    newActivity.getClass().getSimpleName() + ")"
                                );
                            }
                        }

                        ConfigWithoutIntegration.Payments.EventListeners eventListeners = null;
                        if (paymentEventCallback != null) {
                            final Consumer<JsonHelper.PaymentEventType> dispatchEvent = paymentEventType -> {
                                final String jsonStr = JsonHelper.paymentEventToJson(paymentEventType);
                                if (jsonStr == null) {
                                    logError("Failed to convert payment event to JSON string: " + paymentEventType);
                                    return;
                                }

                                paymentEventCallback.onSuccess(jsonStr);
                            };

                            eventListeners = ConfigWithoutIntegration.Payments.EventListeners.getDefault()
                                .withOpenedListener(() -> {
                                    dispatchEvent.accept(JsonHelper.PaymentEventType.Opened);
                                })
                                .withLoadedListener(() -> {
                                    dispatchEvent.accept(JsonHelper.PaymentEventType.Loaded);
                                })
                                .withCompletedListener(() -> {
                                    dispatchEvent.accept(JsonHelper.PaymentEventType.Completed);
                                })
                                .withCancelledListener(() -> {
                                    dispatchEvent.accept(JsonHelper.PaymentEventType.Cancelled);
                                });
                        }

                        return payments
                            .withActivity(newActivity)
                            .withBuyButtonSolutionEnabled(useBuyButtonSolution)
                            .withEmailCollectionConsentOptInEnabled(emailCollectionConsentOptIn)
                            .withQueryCancellationReasonEnabled(queryCancellationReasonEnabled)
                            .withRedirectAppRelaunch(redirectAppRelaunchEnabled)
                            .withEventListeners(eventListeners)
                            .withDomainOverrideConfig(domainOverrideConfig);
                    })
                )
                .orElse(null)
            )
            .setListener(this)
            .build();

        billingClientHolder.set(billingClient);

        billingClient.startConnection(new BillingClientStateListener() {
            @Override
            public void onBillingServiceDisconnected() {
                m_stateCallback.onError("Billing client has disconnected from the services.");
            }

            @Override
            public void onBillingSetupFinished(@NonNull final BillingResult result) {
                if (result.getResponseCode() != BillingClient.BillingResponseCode.OK) {
                    m_stateCallback.onError(result.toString());
                } else {
                    m_stateCallback.onSuccess("");
                }
            }
        });
    }

    public void Restore(
        @NonNull final String argsJson,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        logDebug("Restore: " + argsJson);

        if (!useRestore) { // ignore restore

            callback.onSuccess("{\"purchases\": []}");
            return;
        }

        final var billingClient = billingClientHolder.getOrThrowIfNotReady();
        final var json = JsonHelper.parse(argsJson);
        final var productType = json.map(JsonHelper::jsonToProductType);

        final var params = QueryPurchasesParams
            .newBuilder()
            .setProductType(productType.orElse(BillingClient.ProductType.INAPP))
            .build();

        billingClient.queryPurchasesAsync(params, (result, purchases) -> {
            if (result.getResponseCode() != BillingClient.BillingResponseCode.OK || purchases == null ) {
                callback.onError(result.toString());
                return;
            }

            final var resultJsonObject = JsonHelper.purchasesToJson(purchases);
            if (resultJsonObject.isPresent()) {
                callback.onSuccess(resultJsonObject.get().toString());
            } else {
                callback.onError("Failed to prepare the resulting JSON for purchase querying.");
            }
        });
    }

    /** @noinspection unused*/
    public void ProductsRequest(
        @NonNull final String argsJson,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        logDebug("ProductsRequest: " + argsJson);

        final var billingClient = billingClientHolder.getOrThrowIfNotReady();
        final var json = JsonHelper.parse(argsJson);
        final var products = json.map(JsonHelper::jsonToProducts);

        final var params =
            QueryProductDetailsParams
                .newBuilder()
                .setProductList(products.orElse(Collections.emptyList()))
                .build();

        billingClient.queryProductDetailsAsync(params, (result, productDetailsList) -> {
            if (result.getResponseCode() != BillingClient.BillingResponseCode.OK || productDetailsList == null ) {
                callback.onError(result.toString());
                return;
            }

            var resultJsonObject = JsonHelper.productsToJson(productDetailsList);
            if (resultJsonObject.isPresent())
                callback.onSuccess(resultJsonObject.get().toString());
            else
                callback.onError("Failed to prepare the resulting JSON for query products.");
        });
    }

    /** @noinspection unused*/
    public void Purchase(@NonNull final String argsJson) {
        logDebug("Purchase: " + argsJson);

        final var billingClient = billingClientHolder.getOrThrowIfNotReady();
        final var json = JsonHelper.parse(argsJson);
        final var sku = json.map(JsonHelper::jsonToPurchaseSKU).orElse("");
        final var maybeDeveloperPayload = json.flatMap(JsonHelper::jsonToDeveloperPayload);
        final var maybeExternalTransactionId = json.flatMap(__ ->
            JsonHelper.jsonToString(__, "externalId").filter(s -> !TextUtils.isEmpty(s))
        );
        final Optional<String> maybePaymentToken = json.flatMap(__ ->
            JsonHelper.jsonToString(__, "paymentToken").filter(s -> !TextUtils.isEmpty(s))
        );
        final Optional<Integer> maybePaymentMethodId = json.flatMap(__ ->
            JsonHelper.jsonToInt(__, "paymentMethodId")
        );
        final Optional<Boolean> maybeAllowTokenOnlyFinishedStatusWithoutOrderId = json.flatMap(__ ->
            JsonHelper.jsonToBoolean(__, "allowTokenOnlyFinishedStatusWithoutOrderId")
        );

        if (simpleMode == SimpleMode.ServerTokens || maybePaymentToken.isPresent()) {
            BillingResult billingResult;

            if (maybePaymentToken.isPresent()) {
                try {
                    final BillingFlowCanceller purchaseCanceller = new BillingFlowCanceller();

                    currentPurchaseCanceller.set(purchaseCanceller);

                    final BillingFlowTokenParams params = BillingFlowTokenParams.newBuilder()
                        .setPaymentToken(maybePaymentToken.get())
                        .setDeveloperPayload(maybeDeveloperPayload.orElse(null))
                        .setInvokePurchasesUpdatedIfOrderIdMissingOverride(
                            maybeAllowTokenOnlyFinishedStatusWithoutOrderId.orElse(false)
                        )
                        .setCanceller(purchaseCanceller)
                        .build();
                    billingResult = billingClient.launchBillingFlow(activity, params);
                } catch (Exception e) {
                    billingResult = Error.of(e,
                        "Failed to launch a token based billing flow (token=%s)",
                        maybePaymentToken.get()
                    ).toBillingResult();
                }
            } else {
                billingResult = Error.of(
                    "'SimpleMode' is set to 'ServerTokens', but the token wasn't provided"
                ).toBillingResult();
            }

            if (!billingResult.isSuccessful()) {
                logError(billingResult.toString());

                final BillingResult finalBillingResult = billingResult;

                mainThreadHandler.post(() -> {
                    if (m_purchaseCallback != null) {
                        m_purchaseCallback.onError(JsonHelper.billingResultToJson(finalBillingResult));
                    }
                });
            }
        } else if (simpleMode == SimpleMode.WebShop) {
            final CompletableFuture<Either<Error, Void>> future = new CompletableFuture<>();

            if (TextUtils.isEmpty(webshopUrl)) {
                future.complete(Either.left(Error.of(
                    "Webshop billing flow mode is enabled, but webshop URL wasn't provided"
                )));
            } else if (TextUtils.isEmpty(userId)) {
                future.complete(Either.left(Error.of(
                    "Webshop billing flow mode is enabled, but user ID wasn't provided"
                )));
            } else if (TextUtils.isEmpty(redirectUrl)) {
                future.complete(Either.left(Error.of(
                    "Webshop billing flow mode is enabled, but redirect URL wasn't provided"
                )));
            } else {
                WebShop.launchBillingFlow(
                    activity,
                    webshopUrl,
                    sku,
                    userId,
                    redirectUrl,
                    future::complete
                );
            }

            if (m_purchaseCallback != null) {
                future.thenAcceptAsync(either -> {
                    mainThreadHandler.post(() -> {
                        either.voidFold(
                            err -> m_purchaseCallback.onError(
                                JsonHelper.billingResultToJson(err.toBillingResult())
                            ),
                            __ -> { /* no-op on success */ }
                        );
                    });
                });
            }
        } else {
            final QueryProductDetailsParams params = QueryProductDetailsParams.newBuilder()
                .setProductList(Collections.singletonList(
                    QueryProductDetailsParams.Product.newBuilder()
                        .setProductId(sku)
                        .setProductType(BillingClient.ProductType.INAPP)
                        .build()
                ))
                .build();

            billingClient.queryProductDetailsAsync(params, (result, productDetailsList) -> {
                if (result.getResponseCode() != BillingClient.BillingResponseCode.OK || productDetailsList == null) {
                    logError(result.toString());

                    mainThreadHandler.post(() -> {
                        if (m_purchaseCallback != null) {
                            m_purchaseCallback.onError(JsonHelper.billingResultToJson(result));
                        }
                    });
                } else {
                    final BillingFlowCanceller purchaseCanceller = new BillingFlowCanceller();

                    currentPurchaseCanceller.set(purchaseCanceller);

                    billingClient.launchBillingFlow(activity, BillingFlowParams.newBuilder()
                        .setProductDetailsParamsList(Collections.singletonList(
                            BillingFlowParams.ProductDetailsParams
                                .newBuilder()
                                .setProductDetails(productDetailsList.get(0))
                                .build()
                        ))
                        .setDeveloperPayload(maybeDeveloperPayload.orElse(null))
                        .setExternalTransactionId(maybeExternalTransactionId.orElse(null))
                        .setForcePaymentMethodId(maybePaymentMethodId.orElse(null))
                        .setTrackingId(trackingId)
                        .setCanceller(purchaseCanceller)
                        .build()
                    );
                }
            });
        }
    }

    /** @noinspection unused*/
    public void CancelActivePurchase(
        @NonNull final String argsJson,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        logDebug("CancelActivePurchase: " + argsJson);

        final BillingFlowCanceller billingFlowCanceller = currentPurchaseCanceller.getAndSet(null);
        if (billingFlowCanceller != null) {
            billingFlowCanceller.cancel();

            logDebug("CancelActivePurchase: Successfully scheduled active purchase cancellation");
        }
    }

    /** @noinspection unused*/
    public void Consume(
        @NonNull final String argsJson,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        logDebug("Consume: " + argsJson);

        final var billingClient = billingClientHolder.getOrThrowIfNotReady();
        final var maybeJson = JsonHelper.parse(argsJson);
        final Optional<String> maybePurchaseToken = maybeJson.flatMap(json -> {
            try {
                return Optional.of(json.getString("receipt"));
            } catch (Exception e) {
                Log.e(TAG, "Failed to parse the consume arguments: " + argsJson);
                return Optional.empty();
            }
        });

        if (maybePurchaseToken.isPresent()) {
            final var params =
                ConsumeParams
                    .newBuilder()
                    .setPurchaseToken(maybePurchaseToken.get())
                    .build();

            billingClient.consumeAsync(params, (result, purchaseToken_) -> {
                if (result.getResponseCode() != BillingClient.BillingResponseCode.OK) {
                    callback.onError(result.toString());
                    return;
                }

                var resultJsonObject = JsonHelper.consumeToJson(purchaseToken_);
                if (resultJsonObject.isPresent())
                    callback.onSuccess(resultJsonObject.get().toString());
                else
                    callback.onError("Failed to prepare the resulting JSON for consume.");
            });
        } else {
            callback.onError("A valid purchase token is required for consumption: " + argsJson);
        }
    }

    public void Validate(
        @NonNull final String argsJson,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        logDebug("Validate: " + argsJson);

        final var billingClient = billingClientHolder.getOrThrowIfNotReady();
        final var maybeJson = JsonHelper.parse(argsJson);
        final var maybePurchaseTokenWithNeedValidation = maybeJson.flatMap(json -> Optional
            .of(json.optString("receipt", ""))
            .filter(receiptStr -> !TextUtils.isEmpty(receiptStr))
            .flatMap(receiptStr -> Optional
                .of(json.optString("orderStatus", ""))
                .map(orderStatusStr -> {
                    final var needValidation = TextUtils.isEmpty(orderStatusStr) ||
                        orderStatusStr.compareToIgnoreCase("restored") != 0;
                    return Pair.create(receiptStr, needValidation);
                })
            )
        );

        if (maybePurchaseTokenWithNeedValidation.isPresent()) {
            final var purchaseTokenWithNeedValidation = maybePurchaseTokenWithNeedValidation.get();
            final var purchaseToken = purchaseTokenWithNeedValidation.first;
            final var needValidation = purchaseTokenWithNeedValidation.second;

            if (needValidation) {
                final var params = ValidateParams
                    .newBuilder()
                    .setPurchaseToken(purchaseToken)
                    .build();

                billingClient.validateAsync(params, (result, purchaseToken_) -> {
                    if (result.getResponseCode() != BillingClient.BillingResponseCode.OK) {
                        callback.onError(result.toString());
                    } else {
                        final var maybeNativeResult = JsonHelper.NativeValidateResult.parse(purchaseToken_);
                        if (maybeNativeResult.isPresent()) {
                            final var maybeResultJson = maybeNativeResult.flatMap(JsonHelper::validateToJson);
                            if (maybeResultJson.isPresent()) {
                                logDebug(String.format(
                                    Locale.getDefault(),
                                    "Purchase has been successfully validated (purchaseToken='%s')",
                                    purchaseToken_
                                ));
                                callback.onSuccess(maybeResultJson.get().toString());
                            } else {
                                callback.onError("Failed to prepare the resulting JSON for validate.");
                            }
                        } else {
                            callback.onError(
                                "Validation callback has received an invalid " +
                                "purchase token, yet there was no error."
                            );
                        }
                    }
                });
            } else {
                logDebug(String.format(
                    Locale.getDefault(),
                    "Validation has automatically succeeded for a restored " +
                        "purchase (purchaseToken='%s')",
                    purchaseToken
                ));
                final var maybeResultJson = JsonHelper.NativeValidateResult
                    .parse(purchaseToken)
                    .flatMap(JsonHelper::validateToJson)
                    .map(JSONObject::toString);
                if (maybeResultJson.isPresent()) {
                    callback.onSuccess(maybeResultJson.get());
                } else {
                    callback.onError("Failed to parse the validation result: " + purchaseToken);
                }
            }
        } else {
            callback.onError("Validation requires a valid purchase token.");
        }
    }

    /** @noinspection unused*/
    public void Deinitialize(
        @NonNull final String argsJson,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        logDebug("Deinitialize");

        m_stateCallback = null;
        m_purchaseCallback = null;

        if (billingClientHolder.shutdown()) {
            callback.onSuccess("");
        }
    }

    public void GetAccessToken(
        @NonNull final String argsJson,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        logDebug("GetAccessToken: " + argsJson);

        final var billingClient = billingClientHolder.getOrThrowIfNotReady();

        billingClient.getAuthenticationToken((billingResult, authToken) -> {
            logDebug(String.format(Locale.getDefault(),
                "GetAccessToken: Response: BillingResult=%s Token='%s'",
                billingResult, authToken != null ? authToken.toString() : ""
            ));

            final var maybeJson = Optional
                .ofNullable(authToken)
                .filter(__ -> billingResult.isSuccessful())
                .flatMap(authToken_ -> JsonHelper.tokenToJson(authToken_.toString()))
                .map(JSONObject::toString);

            if (maybeJson.isPresent()) {
                final var jsonStr = maybeJson.get();
                logDebug("GetAccessToken: Response: Json=\n" + jsonStr);
                callback.onSuccess(jsonStr);
            } else {
                callback.onError(billingResult.toString());
            }
        });
    }

    public void UpdateAccessToken(
        @NonNull final String argsJson, // just token
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        logDebug("UpdateAccessToken: " + argsJson);

        final JWTRefresher jwtRefresher_ = jwtRefresher.get();
        if (jwtRefresher_ != null) {
            JWT.parse(argsJson).voidFold(
                // Left.
                err -> {
                    callback.onError("Failed to update access token: " + err.toString());
                },
                // Right.
                jwt -> {
                    jwtRefresher_.update(jwt);

                    callback.onSuccess("");
                }
            );
        } else {
            callback.onError("Access token update is not supported for current authentication method.");
        }
    }

    public void LoginWithSocialAccount(
        @NonNull final String argsJson,
        @Nullable final String provider,
        @Nullable final String accountToken,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    )
    {
        logDebug("LoginWithSocialAccount: args=" + argsJson);
        logDebug("LoginWithSocialAccount: provider=" + provider);
        logDebug("LoginWithSocialAccount: accountToken=" + accountToken);

        final CompletableFuture<LoginResult> future = new CompletableFuture<>();

        CompletableFuture.runAsync(() -> {
            final Optional<Config.Integration.Xsolla.Authentication> maybeAuthConfig =
                JsonHelper.parse(argsJson)
                    .flatMap(JsonHelper::jsonToConfig)
                    .flatMap(config -> {
                        final Config.Integration integration = config.getIntegration();
                        if (integration instanceof Config.Integration.Xsolla) {
                            return Optional.of(((Config.Integration.Xsolla) integration).getAuthentication());
                        } else {
                            return Optional.empty();
                        }
                    });

            if (maybeAuthConfig.isPresent()) {
                final Config.Integration.Xsolla.Authentication authConfig = maybeAuthConfig.get();

                final Optional<LoginUtils.SocialProvider> maybeSocialProvider =
                    Optional.ofNullable(provider).flatMap(provider_ ->
                        LoginUtils.SocialProvider.parse(provider_).asRight()
                    );

                if (maybeSocialProvider.isPresent()) {
                    final LoginUtils.SocialProvider socialProvider = maybeSocialProvider.get();

                    final Optional<String> maybeSocialAccessToken = Optional
                        .ofNullable(accountToken)
                        .filter(str -> !TextUtils.isEmpty(str));

                    if (maybeSocialAccessToken.isPresent()) {
                        final String socialAccessToken = maybeSocialAccessToken.get();

                        final CompletableFuture<Either<Error, LoginInfo>> loginFuture = new CompletableFuture<>();

                        final Optional<Config.Integration.Xsolla.Authentication.ForOAuth2> maybeForOAuth2 = authConfig.asForOAuth2();

                        if (maybeForOAuth2.isPresent()) {
                            final Config.Integration.Xsolla.Authentication.ForOAuth2 forOAuth2 = maybeForOAuth2.get();

                            LoginUtils.loginWithSocialTokenAsync(
                                socialProvider, socialAccessToken, forOAuth2.getOAuth2ClientId(),
                                loginFuture::complete
                            );
                        } else {
                            final Optional<Config.Integration.Xsolla.Authentication.ForAutoJWT> maybeForAutoJWT = authConfig.asForAutoJWT();
                            if (maybeForAutoJWT.isPresent()) {
                                final Config.Integration.Xsolla.Authentication.ForAutoJWT forAutoJWT = maybeForAutoJWT.get();

                                LoginUtils.loginWithSocialTokenAsync(
                                    socialProvider, socialAccessToken, forAutoJWT.getLoginUuid(),
                                    loginFuture::complete
                                );
                            } else {
                                loginFuture.complete(Either.left(Error.of(
                                    "Unsupported authentication method, cannot perform social access token login (method=%s)",
                                    authConfig.getClass().getSimpleName()
                                )));
                            }
                        }

                        loginFuture.thenAcceptAsync(either ->
                            future.complete(either.fold(
                                // Left.
                                err -> new LoginResult.Failure(err.toString()),
                                // Right.
                                loginInfo -> new LoginResult.Success(
                                    loginInfo.getToken().toString(),
                                    loginInfo.getRefreshToken().orElse(null),
                                    loginInfo.getTokenExpiryTime().orElse(new Date())
                                )
                            ))
                        );
                    } else {
                        future.complete(new LoginResult.Failure(
                            "Social access token is invalid"
                        ));
                    }
                } else {
                    future.complete(new LoginResult.Failure(
                        String.format(Locale.getDefault(),
                            "Invalid social provider (name=%s)", provider
                        ))
                    );
                }
            } else {
                future.complete(new LoginResult.Failure(
                    "No valid authentication configuration provided"
                ));
            }
        });

        future.thenAcceptAsync(result ->
            mainThreadHandler.post(() -> result.voidFold(
                success -> {
                    try {
                        callback.onSuccess(success.toJson(true).toString());
                    } catch (Exception e) {
                        callback.onError(String.format(Locale.getDefault(),
                            "Failed to convert authentication data to JSON: " + e
                        ));
                    }
                },
                failure -> callback.onError(failure.msg)
            ))
        );
    }

    public void LoginWithWidget(
        @NonNull final String argsJson,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        final var forceNonSilent = forceNonSilentNextWidgetLogin.getAndSet(false);
        final var openMode = forceNonSilent
            ? LoginXsollaWidget.OpenMode.Interactive
            : LoginXsollaWidget.OpenMode.SilentWithInteractiveFallback;
        LoginWithWidgetImpl(argsJson, openMode, "LoginWithWidget", callback);
    }

    public void LoginSilently(
        @NonNull final String argsJson,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        LoginWithWidgetImpl(argsJson, LoginXsollaWidget.OpenMode.Silent, "LoginSilently", callback);
    }

    public void RefreshToken(
        @NonNull final String argsJsonStr,
        @NonNull final String loginInfoJsonStr,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        logDebug("RefreshToken: args=" + argsJsonStr);
        logDebug("RefreshToken: loginInfo=" + loginInfoJsonStr);

        final var future = new CompletableFuture<LoginResult>();

        CompletableFuture.runAsync(() -> {
            final var maybeArgs = JsonHelper
                .parse(argsJsonStr)
                .flatMap(JsonHelper::parseLoginWidgetArgs);

            final var maybeArgsWithLoginInfo = maybeArgs.flatMap(args ->
                JsonHelper.parse(loginInfoJsonStr).flatMap(json -> Optional
                    .ofNullable(LoginResult.Success.parseJson(json, true))
                    .flatMap(loginResult -> AuthToken
                        .of(loginResult.accessToken)
                        .mapRight(authToken -> Pair.create(args,
                            new LoginInfo(
                                authToken,
                                loginResult.refreshToken,
                                loginResult.accessTokenExpiryTime
                            )
                        ))
                        .fold(
                            err -> {
                                logError(String.format(Locale.getDefault(),
                                    "[RefreshToken] Failed to parse the login token:\n%s",
                                    err
                                ));
                                return Optional.empty();
                            },
                            Optional::of
                        )
                    )
                )
            );

            if (maybeArgsWithLoginInfo.isPresent()) {
                final var argsWithLoginInfo = maybeArgsWithLoginInfo.get();

                LoginXsollaWidget.refreshToken(
                    argsWithLoginInfo.second,
                    activity,
                    argsWithLoginInfo.first,
                    true,
                    either -> future.complete(either.fold(
                        // Error.
                        err -> new LoginResult.Failure(err.toString()),
                        // Success.
                        result -> new LoginResult.Success(
                            result.getToken().toString(),
                            result.getRefreshToken().orElse(null),
                            result.getTokenExpiryTime().orElse(new Date())
                        )
                    ))
                );
            } else {
                future.complete(new LoginResult.Failure(
                    "No valid authentication configuration provided."
                ));
            }
        });

        future.thenAcceptAsync(result ->
            mainThreadHandler.post(() -> result.voidFold(
                success -> {
                    try {
                        callback.onSuccess(success.toJson(true).toString());
                    } catch (Exception e) {
                        callback.onError("Failed to refresh token:\n" + e);
                    }
                },
                failure -> callback.onError(failure.msg)
            ))
        );
    }

    public void ClearToken(
        @NonNull final String argsJson,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        logDebug(String.format(Locale.getDefault(),
            "[ClearToken] Clearing token (args='%s')..",
            argsJson
        ));

        forceNonSilentNextWidgetLogin.set(true);

        mainThreadHandler.post(() -> {
            try {
                callback.onSuccess(XsollaUnityBridgeJsonCallbackUtil.EMPTY_RESULT);
            } catch (Exception e) {
                callback.onError(String.format(Locale.getDefault(),
                    "[ClearToken] Failed to serialize the result data into JSON: " + e
                ));
            }
        });
    }

    public static String GetInstallerPackageName(@NonNull final Context context) {
        return IntegrationUtils.getInstallerPackageName(context).getRightOr("");
    }

    public static boolean IsInstallerPackageName(
        @NonNull final Context context, @NonNull final String packageName
    ) {
        return IntegrationUtils.isInstallerPackageName(context, packageName);
    }

    public static boolean IsInstalledFromGooglePlayStore(@NonNull final Context context) {
        return IntegrationUtils.isInstalledFromGooglePlayStore(context);
    }

    public static boolean IsGooglePlayStoreInstalled(@NonNull final Context context) {
        return IntegrationUtils.isGooglePlayStoreInstalled(context);
    }

    public static void QueryGooglePlayCountryCodeAsync(
        @NonNull final Context context,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        IntegrationUtils.queryGooglePlayCountryCodeAsync(context, either -> {
            either.voidFold(
                // Left.
                err -> {
                    callback.onError(err.toString());
                },
                // Right
                callback::onSuccess
            );
        });
    }

    @Override
    public void onPurchasesUpdated( @NonNull final BillingResult result, @Nullable final List<Purchase> purchases ) {
        if (result.getResponseCode() != BillingClient.BillingResponseCode.OK || purchases == null) {
            m_purchaseCallback.onError(JsonHelper.billingResultToJson(result));
            return;
        }

        final var resultJsonObject = JsonHelper.purchasesToJson(purchases);
        if (resultJsonObject.isPresent()) {
            m_purchaseCallback.onSuccess(resultJsonObject.get().toString());
        } else {
            m_purchaseCallback.onError(JsonHelper.billingResultToJson(
                Error.of("Failed to prepare the resulting JSON for purchase.").toBillingResult()
            ));
        }
    }

    private void LoginWithWidgetImpl(
        @NonNull final String argsJson,
        @NonNull final LoginXsollaWidget.OpenMode openMode,
        @NonNull final String logTag,
        @NonNull final IXsollaUnityBridgeJsonCallback callback
    ) {
        logDebug("LoginWithWidget: " + argsJson);

        final var future = new CompletableFuture<LoginResult>();

        CompletableFuture.runAsync(() -> {
            final var maybeArgs = JsonHelper.parse(argsJson)
                .flatMap(JsonHelper::parseLoginWidgetArgs)
                .map(args -> args.withOpenMode(openMode));
            if (maybeArgs.isPresent()) {
                LoginXsollaWidget.start(activity, maybeArgs.get(), either ->
                    future.complete(either.fold(
                        // Error.
                        err -> new LoginResult.Failure(err.toString()),
                        // Success.
                        result -> new LoginResult.Success(
                            result.getToken().toString(),
                            result.getRefreshToken().orElse(null),
                            result.getTokenExpiryTime().orElse(new Date())
                        )
                    )
                ));
            } else {
                future.complete(new LoginResult.Failure(
                    "No valid authentication configuration provided."
                ));
            }
        });

        future.thenAcceptAsync(result ->
            mainThreadHandler.post(() -> result.voidFold(
                success -> {
                    try {
                        callback.onSuccess(success.toJson(true).toString());
                    } catch (Exception e) {
                        callback.onError(String.format(Locale.getDefault(),
                            "Failed to convert authentication data to JSON: " + e
                        ));
                    }
                },
                failure -> callback.onError(failure.msg)
            ))
        );
    }

    private static final class JsonHelper {
        public enum PaymentEventType {
            Opened,
            Loaded,
            Completed,
            Cancelled
        }

        @NonNull
        public static Optional<JSONObject> parse(@NonNull final String str) {
            Optional<JSONObject> parsedJson;

            try {
                parsedJson = Optional.of(new JSONObject(str.isEmpty() ? "{}" : str));
            } catch (Exception e) {
                parsedJson = Optional.empty();

                logError(String.format("Failed to parse JSON (%s)", e));
            }

            return parsedJson;
        }

        @NonNull
        public static Optional<JSONObject> getChildObject(@NonNull final JSONObject json, @NonNull final String name) {
            Optional<JSONObject> childJson;

            try {
                final var child = json.optJSONObject(name);
                if (child != null)
                    childJson = Optional.of(child);
                else
                    childJson = Optional.empty();
            } catch (Exception e) {
                childJson = Optional.empty();

                logError(String.format("Failed to get child JSON (%s)", e));
            }

            return childJson;
        }

        public static Optional<List<String>> getStringList(
            @NonNull final JSONObject json, @NonNull final String key
        ) {
            return getStrings(json, key, ArrayList::new).map(__ -> (List<String>) __);
        }

        public static Optional<Set<String>> getStringSet(
            @NonNull final JSONObject json, @NonNull final String key
        ) {
            return getStrings(json, key, HashSet::new).map(__ -> (Set<String>) __);
        }

        public static Optional<Collection<String>> getStrings(
            @NonNull final JSONObject json, @NonNull final String key,
            @NonNull final Function<Integer, Collection<String>> createCollection
        ) {
            return Optional.ofNullable(json.optJSONArray(key))
                .flatMap(jsonArray -> {
                    final Collection<String> elements = createCollection.apply(jsonArray.length());

                    for (int i = 0; i < jsonArray.length(); ++i) {
                        final String str = jsonArray.optString(i);
                        if (str != null && !str.isBlank()) {
                            elements.add(str);
                        }
                    }

                    return elements.isEmpty() ? Optional.empty() : Optional.of(elements);
                });
        }

        @Nullable
        public static RetryPolicies parseRetryPolicies(@Nullable final JSONObject json) {
            return Optional.ofNullable(json)
                .flatMap(json_ -> {
                    try {
                        final Optional<RetryProfile> maybeDefaultRetryProfileOverride =
                            getChildObject(json_, "defaultRetryProfileOverride")
                                .flatMap(__ -> Optional.ofNullable(JsonHelper.parseRetryProfile(__)));
                        final Optional<RetryProfile> maybePendingOrderStatusQueryRetryProfileOverride =
                            getChildObject(json_, "pendingOrderStatusQueryRetryProfileOverride")
                                .flatMap(__ -> Optional.ofNullable(JsonHelper.parseRetryProfile(__)));
                        final Optional<RetryProfile> maybeCreateOrderRetryProfileOverride =
                            getChildObject(json_, "createOrderRetryProfileOverride")
                                .flatMap(__ -> Optional.ofNullable(JsonHelper.parseRetryProfile(__)));
                        final Optional<RetryProfile> maybeAuthenticateRetryProfileOverride =
                            getChildObject(json_, "authenticateRetryProfileOverride")
                                .flatMap(__ -> Optional.ofNullable(JsonHelper.parseRetryProfile(__)));
                        final Optional<RetryProfile> maybeQueryProductsRetryProfileOverride =
                            getChildObject(json_, "queryProductsRetryProfileOverride")
                                .flatMap(__ -> Optional.ofNullable(JsonHelper.parseRetryProfile(__)));
                        final Optional<RetryProfile> maybeQueryPurchasesRetryProfileOverride =
                            getChildObject(json_, "queryPurchasesRetryProfileOverride")
                                .flatMap(__ -> Optional.ofNullable(JsonHelper.parseRetryProfile(__)));
                        final Optional<RetryProfile> maybeConsumePurchasesRetryProfileOverride =
                            getChildObject(json_, "consumePurchasesRetryProfileOverride")
                                .flatMap(__ -> Optional.ofNullable(JsonHelper.parseRetryProfile(__)));

                        if (maybeDefaultRetryProfileOverride.isPresent() ||
                            maybePendingOrderStatusQueryRetryProfileOverride.isPresent() ||
                            maybeCreateOrderRetryProfileOverride.isPresent() ||
                            maybeAuthenticateRetryProfileOverride.isPresent() ||
                            maybeQueryProductsRetryProfileOverride.isPresent() ||
                            maybeQueryPurchasesRetryProfileOverride.isPresent() ||
                            maybeConsumePurchasesRetryProfileOverride.isPresent()
                        ) {
                            return Optional.of(RetryPolicies.getDefault()
                                .withDefaultRetryProfileOverride(maybeDefaultRetryProfileOverride.orElse(null))
                                .withPendingOrderStatusQueryRetryProfileOverride(maybePendingOrderStatusQueryRetryProfileOverride.orElse(null))
                                .withCreateOrderRetryProfileOverride(maybeCreateOrderRetryProfileOverride.orElse(null))
                                .withAuthenticateRetryProfileOverride(maybeAuthenticateRetryProfileOverride.orElse(null))
                                .withQueryProductsRetryProfileOverride(maybeQueryProductsRetryProfileOverride.orElse(null))
                                .withQueryPurchasesRetryProfileOverride(maybeQueryPurchasesRetryProfileOverride.orElse(null))
                                .withConsumePurchasesRetryProfileOverride(maybeConsumePurchasesRetryProfileOverride.orElse(null))
                            );
                        }

                        return Optional.empty();
                    } catch (Exception e) {
                        logDebug("Failed to parse a retry policies (json=" + json_ + ")\n" + e);
                        return Optional.empty();
                    }
                })
                .orElse(null);
        }

        @Nullable
        public static RetryProfile parseRetryProfile(@Nullable final JSONObject json) {
            return Optional.ofNullable(json)
                .flatMap(json_ -> {
                    try {
                        return Optional.of(json_.getString("type"))
                            .filter(__ -> !__.isBlank())
                            .flatMap(type -> {
                                if (type.compareToIgnoreCase("uniform") == 0) {
                                    try {
                                        final long maxNumAttempts = json_.getLong("maxNumAttempts");
                                        final long intervalMillis = json_.getLong("intervalMillis");
                                        return Optional.of(RetryProfile.uniform(
                                            PositiveInteger.create((int) maxNumAttempts).getRightOrThrow(),
                                            PositiveInteger.create((int) intervalMillis).getRightOrThrow()
                                        ));
                                    } catch (Exception e) {
                                        logDebug("Failed to parse uniform retry profile (json=" + json_ + ")\n" + e);
                                        return Optional.empty();
                                    }
                                } else if (type.compareToIgnoreCase("exponentialbackoff") == 0) {
                                    try {
                                        final long maxNumAttempts = json_.getLong("maxNumAttempts");
                                        final long baseIntervalMillis = json_.getLong("baseIntervalMillis");
                                        final long maxIntervalMillis = json_.optLong("maxIntervalMillis", 0);
                                        final long maxRandomExtraDelayMillis = json_.optLong("maxRandomExtraDelayMillis", 0);
                                        return Optional.of(RetryProfile.exponentialBackout(
                                            PositiveInteger.create((int) maxNumAttempts).getRightOrThrow(),
                                            PositiveInteger.create((int) baseIntervalMillis).getRightOrThrow(),
                                            maxIntervalMillis > 0 ? PositiveInteger.create((int) maxIntervalMillis).getRightOrThrow() : null,
                                            maxRandomExtraDelayMillis > 0 ? PositiveInteger.create((int) maxRandomExtraDelayMillis).getRightOrThrow() : null
                                        ));
                                    } catch (Exception e) {
                                        logDebug("Failed to parse uniform retry profile (json=" + json_ + ")\n" + e);
                                        return Optional.empty();
                                    }
                                } else {
                                    logDebug("Tried to a parse retry profile, but encountered " +
                                        "an unknown type '" + type + "' (json=" + json_ + ")"
                                    );
                                    return Optional.empty();
                                }
                            });
                    } catch (Exception e) {
                        logDebug("Failed to parse a retry profile (json=" + json_ + ")\n" + e);
                        return Optional.empty();
                    }
                })
                .orElse(null);
        }

        private static final class SocialProviderInfo {
            @NonNull
            private final LoginUtils.SocialProvider provider;

            @NonNull
            private final String accessToken;

            @NonNull
            public LoginUtils.SocialProvider getProvider() { return provider; }

            @NonNull
            public String getAccessToken() { return accessToken; }

            @Nullable
            public static SocialProviderInfo parse(@NonNull final JSONObject json) {
                try {
                    final String name = json.getString("name");
                    final Either<Error, LoginUtils.SocialProvider> providerEither = LoginUtils.SocialProvider.parse(name);

                    if (providerEither.isLeft()) {
                        logError(String.format(Locale.getDefault(),
                            "Failed to parse social provider name (%s): %s",
                            name, providerEither.getLeft()
                        ));
                        return null;
                    } else {
                        assert providerEither.isRight();
                        final String accessToken = json.getString("accessToken");
                        return new SocialProviderInfo(providerEither.getRight(), accessToken);
                    }
                } catch (Exception e) {
                    logError("Failed to parse social provider information (" + e + ")");
                    return null;
                }
            }

            private SocialProviderInfo(
                @NonNull final LoginUtils.SocialProvider provider, @NonNull final String accessToken
            ) {
                this.provider = provider;
                this.accessToken = accessToken;
            }
        }

        @NonNull
        private static Optional<Config> jsonToConfig(@NonNull final JSONObject json) {
            final var settings = getChildObject(json,"settings").orElse(null);
            if (settings == null) {
                logError("settings not found");
                return Optional.empty();
            }

            final var maybeUiSettingsJson = getChildObject(settings,"uiSettings");
            final var redirectSettings = getChildObject(settings,"redirectSettings");

            final var projectId = ProjectId.parse(settings.optInt("projectId", -1));
            final var loginUuid = LoginUuid.parse(settings.optString("loginId", ""));
            final var oauthId = OAuth2ClientId.parse(settings.optInt("oauthClientId", 0))
                .flatMapRight(id -> Either.ofBoolean(id.toInteger() > 0,
                    () -> Error.forMessage("Wrong OAuth2 client ID"),
                    () -> id
                ));
            final var token = JWT.parse(json.optString("accessToken", ""));
            final var sandbox = json.optBoolean("sandbox", false);
            final var logLevel = json.optString("logLevel", "None");
            final var locale = json.optString("locale", "");
            final var sdkName = json.optString("sdkName", "Unity");
            final var sdkVersion = json.optString("sdkVersion", "unknown");
            final var userId = json.optString("userId", "");
            final String webhooksMode = settings.optString("webhooksMode", "");

            final Optional<SocialProviderInfo> maybeSocialProviderInfo = Optional
                .ofNullable(settings.optJSONObject("socialProvider"))
                .flatMap(json_ -> Optional.ofNullable(SocialProviderInfo.parse(json_)));

            logDebug("SDK: " + sdkName + ", " +  sdkVersion);

            maybeSocialProviderInfo.ifPresent(socialProviderInfo -> {
                logDebug(String.format(Locale.getDefault(),
                    "SocialProvider:\n  * Name: %s\n  * AccessToken: %s",
                    socialProviderInfo.getProvider().getName(),
                    socialProviderInfo.getAccessToken()
                ));
            });

            final boolean fetchPersonalizedProductsOnly = json.optBoolean("fetchPersonalizedProductsOnly", true);
            final boolean fetchProductsWithGeoLocale = json.optBoolean("fetchProductsWithGeoLocale");

            final var config = Config.Common.getDefault()
                .withSandboxEnabled(sandbox)
                .withLocaleOverride(LocaleInfo.parse(locale).getRightOr((LocaleInfo) null))
                .withLogLevel(logLevel.equals("Debug") ? LogLevel.VERBOSE : LogLevel.ERROR)
                .withFetchPersonalizedProductsOnly(fetchPersonalizedProductsOnly)
                .withUseGeoLocaleOnProductQuery(fetchProductsWithGeoLocale);

            final var backText = redirectSettings.map( s -> s.optString("redirectButtonText")).orElse("");
            final var redirectDelay = redirectSettings.map( s -> s.optInt("redirectDelay")).orElse(6);

            @Nullable final Config.Payments.Activity activity;

            switch(settings.optString("webViewType", "").toLowerCase(Locale.getDefault())) {
                case "ingame":
                    activity = Config.Payments.Activity.forWebView();
                    break;

                case "system":
                    activity = Config.Payments.Activity.forCustomTabs();
                    break;

                case "trusted": {
                    final Config.Payments.Activity.TrustedWebActivity.Image image;

                    // Android resource IDs start from 1, where 0 means `invalid ID`.
                    // https://developer.android.com/reference/android/content/res/Resources#ID_NULL
                    final int drawableId = settings.optInt("webViewSplashScreenImageDrawableIdAndroid", 0);
                    if (drawableId > 0) {
                        image = Config.Payments.Activity.TrustedWebActivity.Image.forDrawableId(drawableId);
                    } else {
                        final String filepath = settings.optString("webViewSplashScreenImageFilepath");
                        image = !TextUtils.isEmpty(filepath)
                            ? Config.Payments.Activity.TrustedWebActivity.Image.forFilepath(filepath)
                            : null;
                    }

                    activity = image != null
                        ? Config.Payments.Activity.forTrustedWebActivityWithImage(image)
                        : Config.Payments.Activity.forTrustedWebActivity();

                    break;
                }

                case "external":
                    activity = Config.Payments.Activity.forSystem();
                    break;

                // Auto.
                default:
                    activity = null;
                    break;
            }


            @Nullable final Integer activityOrientationLock;

            switch(settings.optString("webViewOrientationLock", "").toLowerCase(Locale.getDefault())) {
                case "portrait":
                    activityOrientationLock = Config.Payments.ACTIVITY_ORIENTATION_LOCK_PORTRAIT;
                    break;

                case "landscape":
                    activityOrientationLock = Config.Payments.ACTIVITY_ORIENTATION_LOCK_LANDSCAPE;
                    break;

                default:
                    activityOrientationLock = null;
                    break;
            }


            //public enum ThemeSize { Auto, Small, Medium, Large }
            Integer themeSize;
            switch(maybeUiSettingsJson.map(s -> s.optString("themeSize")).orElse("Auto"))
            {
                case "Small":
                    themeSize = Config.Payments.Theme.SIZE_SMALL;
                    break;
                case "Medium":
                    themeSize = Config.Payments.Theme.SIZE_MEDIUM;
                    break;
                case "Large":
                    themeSize = Config.Payments.Theme.SIZE_LARGE;
                    break;
                case "Auto":
                default:
                    themeSize = null;
                    break;
            }
            final var customTheme = maybeUiSettingsJson.map(s -> s.optString("customTheme")).orElse("");
            //public enum ThemeStyle { Auto,  Light, Dark, Custom }
            Config.Payments.Theme.Style themeStyle;
            switch(maybeUiSettingsJson.map(s -> s.optString("themeStyle")).orElse("Auto"))
            {
                case "Light":
                    themeStyle = Config.Payments.Theme.Style.forLight();
                    break;
                case "Dark":
                    themeStyle = Config.Payments.Theme.Style.forDark();
                    break;
                case "Custom":
                   themeStyle = Config.Payments.Theme.Style.forCustom(customTheme);
                   break;
                case "Auto":
                default:
                    themeStyle = null;
                    break;
            }

            final var themeSizeF = themeSize;
            final var themeStyleF = themeStyle;

            //public enum CloseButton {Auto, Show, Hide }
            Config.Payments.CloseButton closeButton;
            switch(settings.optString("closeButton", "Auto"))
            {
                case "Show":
                    closeButton = Config.Payments.CloseButton.show();
                    break;
                case "Hide":
                    closeButton = Config.Payments.CloseButton.hide();
                    break;
                case "Auto":
                default:
                    closeButton = null;
                    break;
            }


            final Boolean showLogoEnabled;

            {
                // public enum VisibleLogo { Auto, Show, Hide }

                final String visibleLogo = maybeUiSettingsJson
                    .map(uiSettingsJson ->
                        uiSettingsJson.optString("visibleLogo").toLowerCase()
                    )
                    .orElse("auto");

                switch (visibleLogo) {
                    case "show":
                        showLogoEnabled = true;
                        break;

                    case "hide":
                        showLogoEnabled = false;
                        break;

                    default:
                        showLogoEnabled = null;
                        break;
                }
            }

            Uri redirectUri = null;

            if (!TextUtils.isEmpty(redirectUrl)) {
                try {
                    redirectUri = Uri.parse(redirectUrl);

                    Log.d(TAG, String.format(Locale.getDefault(),
                        "Redirect URL was overridden: %s", redirectUri.toString()
                    ));
                } catch (Exception e) {
                    Log.d(TAG, String.format(Locale.getDefault(),
                        "Failed to parse redirect URL: %s", redirectUrl
                    ));
                }
            } else {
                Log.d(TAG, "Redirect URL wasn't overridden, will use the defaults");
            }

            final var paymentsConfig = Config.Payments.getDefault()
                .withRedirectButtonText(backText)
                .withRedirectTimeoutSecs(redirectDelay)
                .withRedirectUrl(redirectUri)
                .withShowLogoEnabled(showLogoEnabled)
                .withTheme(theme -> theme
                    .withSize(themeSizeF)
                    .withStyle(themeStyleF)
                )
                .withActivity(activity)
                .withActivityOrientationLock(activityOrientationLock)
                .withCloseButton(closeButton)
                .withCustomUserId(userId.isEmpty() ? null : userId);

            final var analyticsConfig = Config.Analytics.getDefault()
                .withGameEngine(sdkName)
                .withGameEngineVersion(sdkVersion);

            final Config.Integration.Xsolla.Events eventsConfig;
            if (webhooksMode.toLowerCase(Locale.getDefault()).equals("eventsapi")) {
                eventsConfig = Config.Integration.Xsolla.Events
                    .newBuilder()
                    .build();
            } else {
                eventsConfig = null;
            }

            if (token.asRight().isPresent()) {
                Log.d(TAG, String.format(Locale.getDefault(),
                    "Evaluating authentication based on project ID and JWT token (access_token=%s)",
                    token.mapRight(JWT::getToken).getRightOr(__ -> "n/a")
                ));

                maybeSocialProviderInfo.ifPresent(__ -> {
                    logDebug("Social provider data has been provided, but authentication " +
                        "using a social access token is not supported when JWT token is " +
                        "supplied as well"
                    );
                });

                return token.flatMapRight(t ->
                    projectId.mapRight(pid -> {
                        Log.d(TAG, String.format(Locale.getDefault(),
                            "Authenticating based on project ID and JWT token (project_id=%d access_token=%s)",
                            pid.value, t.getToken()
                        ));
                        final JWTRefresher refresher = new JWTRefresher();
                        jwtRefresher.set(refresher);
                        return new Config(config,
                            Config.Integration.forXsolla(
                                Config.Integration.Xsolla.Authentication.forJWT(pid, t, refresher)
                            ).withEvents(eventsConfig), paymentsConfig, analyticsConfig
                        );
                    })
                ).fold(
                    err -> {
                        logError(err.toString());
                        return Optional.empty();
                    },
                    Optional::of
                );
            }
            else if (oauthId.asRight().isPresent()) {
                Log.d(TAG, String.format(Locale.getDefault(),
                    "Evaluating authentication based on project ID, login ID and OAuth2 client ID (oauth2_client_id=%s)",
                    oauthId.mapRight(__ -> String.valueOf(__.toInteger())).getRightOr("n/a")
                ));

                return projectId.flatMapRight(pid ->
                    loginUuid.flatMapRight(lid ->
                        oauthId.mapRight(oid -> {
                            @NonNull final Config.Integration.Xsolla.Authentication authentication;

                            if (maybeSocialProviderInfo.isPresent()) {
                                final SocialProviderInfo socialProviderInfo = maybeSocialProviderInfo.get();

                                Log.d(TAG, String.format(Locale.getDefault(),
                                    "Authenticating using project ID, login ID, OAuth2 client ID and social " +
                                    "token (project_id=%d login_id=%s oauth2_client_id=%d social_token=%s)",
                                    pid.value, lid, oid.toInteger(), socialProviderInfo.accessToken
                                ));

                                authentication = Config.Integration.Xsolla.Authentication.forSocial(
                                    pid, lid, oid, socialProviderInfo.getProvider(), socialProviderInfo.getAccessToken()
                                );
                            } else {
                                Log.d(TAG, String.format(Locale.getDefault(),
                                    "Authenticating using project ID, login ID and OAuth2 client ID based on device ID " +
                                    "(project_id=%d login_id=%s oauth2_client_id=%d)",
                                    pid.value, lid, oid.toInteger()
                                ));

                                authentication = Config.Integration.Xsolla.Authentication.forOAuth2(pid, lid, oid);
                            }

                            return new Config(
                                config,
                                Config.Integration.forXsolla(authentication).withEvents(eventsConfig),
                                paymentsConfig,
                                analyticsConfig
                            );
                        })
                    )
                ).fold(
                    err -> {
                        logError(err.toString());
                        return Optional.empty();
                    },
                    Optional::of
                );
            }
            else {
                Log.d(TAG, "Evaluating authentication based on project ID, login ID");

                return projectId
                    .flatMapRight(pid -> loginUuid.mapRight(luuid -> {
                        @NonNull final Config.Integration.Xsolla.Authentication authentication;

                        if (maybeSocialProviderInfo.isPresent()) {
                            final SocialProviderInfo socialProviderInfo = maybeSocialProviderInfo.get();

                            Log.d(TAG, String.format(Locale.getDefault(),
                                "Authenticating using project ID, login ID and social " +
                                "token (project_id=%d login_id=%s social_token=%s)",
                                pid.value, luuid, socialProviderInfo.accessToken
                            ));

                            authentication = Config.Integration.Xsolla.Authentication.forSocial(
                                pid, luuid, socialProviderInfo.getProvider(), socialProviderInfo.getAccessToken()
                            );
                        } else {
                            Log.d(TAG, String.format(Locale.getDefault(),
                                "Authenticating using project ID, login ID based on device ID " +
                                "(project_id=%d login_id=%s)",
                                pid.value, luuid
                            ));

                            authentication = Config.Integration.Xsolla.Authentication.forAutoJWT(pid, luuid);
                        }

                        return new Config(
                            config,
                            Config.Integration.forXsolla(authentication).withEvents(eventsConfig),
                            paymentsConfig,
                            analyticsConfig
                        );
                    }))
                    .fold(
                        err -> {
                            logError(err.toString());
                            return Optional.empty();
                        },
                        Optional::of
                    );
            }
        }

        private static boolean jsonToDebug(@NonNull final JSONObject json) {
            return json.optBoolean("sandbox", false);
        }

        private static String jsonToLog(@NonNull final JSONObject json) {
            return json.optString("logLevel", "");
        }

        @NonNull
        private static String jsonToProductType(@NonNull final JSONObject json) {
            return json.optString("sku_type", BillingClient.ProductType.INAPP).toLowerCase();
        }

        @NonNull
        private static List<QueryProductDetailsParams.Product> jsonToProducts(@NonNull final JSONObject json) {
            List<QueryProductDetailsParams.Product> products = Collections.emptyList();

            try {
                final var jsonArray = json.optJSONArray("products");
                if (jsonArray != null) {
                    products = new ArrayList<>(jsonArray.length());

                    for (int i = 0; i < jsonArray.length(); ++i) {
                        products.add(
                            QueryProductDetailsParams.Product
                            .newBuilder()
                            .setProductId(jsonArray.getString(i))
                            .setProductType(BillingClient.ProductType.INAPP)
                            .build()
                        );
                    }
                }
            } catch (Exception e) {
                logError(e.toString());
            }

            return products;
        }

        @NonNull
        private static String jsonToPurchaseSKU(@NonNull final JSONObject json) {
            return json.optString("sku", "");
        }

        @NonNull
        private static Optional<String> jsonToDeveloperPayload(@NonNull final JSONObject jsonObject) {
            return jsonToString(jsonObject, "developerPayload");
        }

        @NonNull
        private static Optional<String> jsonToString(
            @NonNull final JSONObject jsonObject, @NonNull final String key
        ) {
            try {
                return Optional.of(jsonObject.getString(key));
            } catch (Exception e) {
                return Optional.empty();
            }
        }

        @NonNull
        private static Optional<Integer> jsonToInt(
            @NonNull final JSONObject jsonObject, @NonNull final String key
        ) {
            try {
                return Optional.of(jsonObject.getInt(key));
            } catch (Exception e) {
                return Optional.empty();
            }
        }

        @NonNull
        private static Optional<Boolean> jsonToBoolean(
            @NonNull final JSONObject jsonObject, @NonNull final String key
        ) {
            try {
                return Optional.of(jsonObject.getBoolean(key));
            } catch (Exception e) {
                return Optional.empty();
            }
        }

        @NonNull
        private static Optional<JSONObject> productToJson(@NonNull final ProductDetails product) {
            JSONObject resultJsonObject;

            try {
                resultJsonObject =
                    new JSONObject()
                    .put("sku", product.getProductId())
                    .put("title", product.getTitle())
                    .put("description", product.getDescription())
                    // getPriceAmountMicros()     — base price, no discounts
                    // getFullPriceAmountMicros() — final price after discounts
                    .put("price", product.getOneTimePurchaseOfferDetails() != null
                        ? product.getOneTimePurchaseOfferDetails().getPriceAmountMicros()
                        : 0L
                    )
                    .put("priceWithDiscount", product.getOneTimePurchaseOfferDetails() != null
                        ? product.getOneTimePurchaseOfferDetails().getFullPriceAmountMicros()
                        : 0L
                    )
                    .put("priceWithoutDiscount", product.getOneTimePurchaseOfferDetails() != null
                        ? product.getOneTimePurchaseOfferDetails().getPriceAmountMicros()
                        : 0L
                    )
                    .put("currency", product.getOneTimePurchaseOfferDetails() != null
                        ? product.getOneTimePurchaseOfferDetails().getPriceCurrencyCode()
                        : ""
                    )
                    .put("formattedPrice", product.getOneTimePurchaseOfferDetails() != null
                        ? product.getOneTimePurchaseOfferDetails().getFormattedPrice()
                        : ""
                    )
                    .put("formattedPriceWithDiscount",
                        product.getOneTimePurchaseOfferDetails() != null && product.getOneTimePurchaseOfferDetails().getDiscountDisplayInfo() != null
                            ? product.getOneTimePurchaseOfferDetails().getDiscountDisplayInfo().getFormattedPrice()
                            : ""
                    )
                    .put("formattedPriceWithoutDiscount", product.getOneTimePurchaseOfferDetails() != null
                        ? product.getOneTimePurchaseOfferDetails().getFormattedPrice()
                        : ""
                    )
                    .put("discountPercentage",
                        product.getOneTimePurchaseOfferDetails() != null
                            && product.getOneTimePurchaseOfferDetails().getDiscountDisplayInfo() != null
                        ? String.valueOf(product.getOneTimePurchaseOfferDetails().getDiscountDisplayInfo().getPercentageDiscount())
                        : ""
                    )
                    .put("iconUrl", product.getIconUrl());
            } catch (Exception e) {
                logError("Failed to convert a product into JSON object: " + e);
                resultJsonObject = null;
            }

            return Optional.ofNullable(resultJsonObject);
        }

        @NonNull
        public static Optional<JSONObject> productsToJson(@NonNull final List<ProductDetails> products) {
            JSONObject resultJsonObject;

            try {

                final var jsonArray = new JSONArray(products
                    .stream()
                    .map( p -> JsonHelper.productToJson(p).orElse(null) )
                    .filter(Objects::nonNull)
                    .collect(Collectors.toList())
                );

                resultJsonObject = new JSONObject()
                        .put("products", jsonArray);


            } catch (Exception e) {
                logError("Failed to convert a products into JSON object: " + e);
                resultJsonObject = null;
            }

            return Optional.ofNullable(resultJsonObject);
        }

        @NonNull
        private static Optional<JSONObject> purchaseToJson(@NonNull final Purchase purchase) {
            final var products = purchase.getProducts();
            final String productId = products.isEmpty() ? null : products.get(0);

            JSONObject resultJsonObject;

            try {
                resultJsonObject = new JSONObject();

                if (!TextUtils.isEmpty(productId)) {
                    resultJsonObject.put("sku", productId);
                }

                resultJsonObject
                    .put("status",
                        purchase.getPurchaseState() == Purchase.PurchaseState.PURCHASED
                            ? "purchased"
                            : "pending"
                    )
                    .put("orderId", purchase.getOriginalOrderId() != null ? purchase.getOriginalOrderId() : "")
                    // invoiceId and transactionId both map to getOrderId() — the Xsolla mobile SDK
                    // does not expose a separate transaction identifier distinct from the invoice ID.
                    .put("invoiceId", purchase.getOrderId() != null ? purchase.getOrderId() : "")
                    .put("transactionId", purchase.getOrderId() != null ? purchase.getOrderId() : "")
                    .put("developerPayload", purchase.getDeveloperPayload() != null ? purchase.getDeveloperPayload() : "")
                    .put("receipt", purchase.getPurchaseToken());
            } catch (Exception e) {
                logError("Failed to convert a purchase into JSON object (" + productId + ")");
                resultJsonObject = null;
            }

            return Optional.ofNullable(resultJsonObject);
        }

        @NonNull
        private static Optional<JSONObject> purchasesToJson(@NonNull final List<Purchase> purchases) {
            JSONObject resultJsonObject;

            try {
                final var jsonArray = new JSONArray(purchases
                    .stream()
                    .map( p -> JsonHelper.purchaseToJson(p).orElse(null) )
                    .filter(Objects::nonNull)
                    .collect(Collectors.toList())
                );
                resultJsonObject = new JSONObject()
                    .put("purchases", jsonArray);

            } catch (Exception e) {
                logError("Failed to convert a purchases into JSON object");
                resultJsonObject = null;
            }

            return Optional.ofNullable(resultJsonObject);
        }

        @NonNull
        private static Optional<JSONObject> consumeToJson(@NonNull final String token) {
            return purchaseTokenToJson(token);
        }

        @NonNull
        private static Optional<JSONObject> validateToJson(
            @NonNull final NativeValidateResult nativeValidateResult
        ) {
            try {
                return Optional.of(new JSONObject().put("success", true));
//            purchaseTokenToJson(nativeValidateResult.purchaseToken);
            } catch (Exception e) {
                logDebug("[validateToJson] " + e);
                return Optional.empty();
            }
        }

        @NonNull
        private static Optional<JSONObject> tokenToJson(@NonNull final String token) {
            JSONObject resultJsonObject;
            try {
                resultJsonObject = new JSONObject()
                                      .put("token", token);
            } catch (Exception e) {
                logDebug("[tokenToJson] " + e);
                resultJsonObject = null;
            }

            return Optional.ofNullable(resultJsonObject);
        }

        @NonNull
        private static Optional<JSONObject> purchaseTokenToJson(@NonNull final String purchaseToken) {
            JSONObject resultJsonObject;
            try {
                resultJsonObject = new JSONObject()
                    .put("purchase_token", purchaseToken);
            } catch (Exception e) {
                logError("Failed to convert a consume into JSON object (" + purchaseToken + ")");
                resultJsonObject = null;
            }
            return Optional.ofNullable(resultJsonObject);
        }

        public static Optional<LoginXsollaWidget.Args> parseLoginWidgetArgs(
            @NonNull final JSONObject json
        ) {
            return JsonHelper.jsonToConfig(json).flatMap(config_ -> {
                final var integration = config_.getIntegration();

                if (integration instanceof Config.Integration.Xsolla) {
                    final var integration_ = (Config.Integration.Xsolla) integration;

                    final Optional<Pair<LoginUuid, OAuth2ClientId>> maybeLoginUuidWithOAuth2ClientId =
                        integration_.getAuthentication().fold(
                            forOAuth2 -> Optional.of(Pair.create(
                                forOAuth2.getLoginUuid(),
                                forOAuth2.getOAuth2ClientId()
                            )),
                            forJWT -> Optional.empty(),
                            forAutoJWT -> Optional.empty(),
                            forWidget -> Optional.of(Pair.create(
                                forWidget.getLoginUuid(),
                                forWidget.getOAuth2ClientId()
                            )),
                            forSocialWithOAuth2 -> Optional.of(Pair.create(
                                forSocialWithOAuth2.getLoginUuid(),
                                forSocialWithOAuth2.getOAuth2ClientId()
                            )),
                            forSocial -> Optional.empty()
                        );

                    return maybeLoginUuidWithOAuth2ClientId.map(tpl ->
                        new LoginXsollaWidget.Args(tpl.first, tpl.second)
                            .withLocale(config_.getCommon().getLocaleOverride().orElse(null))
                    );
                } else {
                    return Optional.empty();
                }
            });
        }

        private static final class NativeValidateResult {
            @NonNull
            private final String purchaseToken;

            @NonNull
            public String getPurchaseToken() { return purchaseToken; }

            @NonNull
            public static Optional<NativeValidateResult> parse(@Nullable final String purchaseToken) {
                return Optional
                    .ofNullable(purchaseToken)
                    .filter(s -> !TextUtils.isEmpty(s))
                    .map(NativeValidateResult::new);
            }

            private NativeValidateResult(@NonNull final String purchaseToken) {
                this.purchaseToken = purchaseToken;
            }
        }

        @NonNull
        public static String billingResultToJson(@NonNull final BillingResult billingResult) {
            final String code;

            switch (billingResult.getResponseCode()) {
                case BillingClient.BillingResponseCode.USER_CANCELED:
                    code = "Cancelled";
                    break;

                case BillingClient.BillingResponseCode.SERVICE_UNAVAILABLE:
                case BillingClient.BillingResponseCode.SERVICE_DISCONNECTED:
                case BillingClient.BillingResponseCode.DEVELOPER_ERROR:
                case BillingClient.BillingResponseCode.BILLING_UNAVAILABLE:
                case BillingClient.BillingResponseCode.ERROR:
                    code = "Internal";
                    break;

                default:
                    code = "Unknown";
                    break;
            }

            return createErrorJson(billingResult.getDebugMessage(), code);
        }

        @NonNull
        public static String createErrorJson(
            @NonNull final String message, @Nullable final String code
        ) {
            return String.format(Locale.getDefault(),
                "{\"message\":\"%s\",\"code\":\"%s\"}",
                message, code
            );
        }

        @Nullable
        public static String paymentEventToJson(@NonNull final PaymentEventType eventType) {
            try {
                return new JSONObject()
                    .put("type", eventType.name())
                    .toString();
            } catch (Exception e) {
                return null;
            }
        }
    }
}
