using System;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Xsolla.SDK.Common.Extensions
{
    [Serializable]
    internal abstract class RetryProfile
    {
        internal enum Type
        {
            Uniform,
            ExponentialBackoff
        }
        
        [Serializable]
        internal class UniformImpl : RetryProfile
        {
            /// <summary>
            /// Maximum number of attempts, including the initial attempt and all retries.
            /// </summary>
            public uint maxNumAttempts;

            /// <summary>
            /// Constant delay in milliseconds between attempts.
            /// </summary>
            public uint intervalMillis;

            public UniformImpl(uint maxNumAttempts, uint intervalMillis)
                : base(Type.Uniform)
            {
                this.maxNumAttempts = maxNumAttempts;
                this.intervalMillis = intervalMillis;
            }
        }

        [Serializable]
        internal class ExponentialBackoffImpl : RetryProfile
        {
            /// <summary>
            /// Maximum number of attempts, including the initial attempt and all retries.
            /// </summary>
            public uint maxNumAttempts;

            /// <summary>
            /// Initial delay in milliseconds before the first retry.
            /// Subsequent delays grow exponentially from this base value.
            /// </summary>
            public uint baseIntervalMillis;

            /// <summary>
            /// Optional maximum delay in milliseconds. When specified, the
            /// backoff delay will not grow beyond this value. When null,
            /// the delay is unbounded (aside from numeric limits).
            /// </summary>
            public uint? maxIntervalMillis;

            /// <summary>
            /// Optional maximum random extra delay in milliseconds added as jitter
            /// to each computed backoff delay. When null, no extra random delay is used.
            /// </summary>
            public uint? maxRandomExtraDelayMillis;

            public ExponentialBackoffImpl(
                uint maxNumAttempts,
                uint baseIntervalMillis,
                uint? maxIntervalMillis,
                uint? maxRandomExtraDelayMillis
            ) : base(Type.ExponentialBackoff)
            {
                this.maxNumAttempts = maxNumAttempts;
                this.baseIntervalMillis = baseIntervalMillis;
                this.maxIntervalMillis = maxIntervalMillis;
                this.maxRandomExtraDelayMillis = maxRandomExtraDelayMillis;
            }
        }

        [JsonProperty, JsonConverter(typeof(StringEnumConverter))]
        internal Type type;

        /// <summary>
        /// A <see cref="RetryProfile"/> that uses a constant delay between all attempts.
        /// </summary>
        /// <remarks>
        /// The number of attempts is limited by <paramref name="maxNumAttempts"/>, and each attempt
        /// (including retries) is separated by the same fixed delay specified by
        /// <paramref name="intervalMillis"/> (in milliseconds).
        /// </remarks>
        /// <param name="maxNumAttempts">Maximum number of attempts, including the initial attempt and all retries.</param>
        /// <param name="intervalMillis">The constant delay in milliseconds between attempts.</param>
        /// <returns>A retry profile configured with a uniform delay.</returns>
        public static RetryProfile Uniform(uint maxNumAttempts, uint intervalMillis)
        {
            return new UniformImpl(maxNumAttempts, intervalMillis);
        }

        /// <summary>
        /// A <see cref="RetryProfile"/> that uses an exponentially increasing backoff delay
        /// between attempts, with optional capping and random jitter.
        /// </summary>
        /// <remarks>
        /// The initial delay is defined by <paramref name="baseIntervalMillis"/>. For each
        /// subsequent attempt, the delay grows exponentially according to the implementation
        /// in <see cref="RetryProfile"/>, and can be optionally capped by
        /// <paramref name="maxIntervalMillis"/> and randomized by
        /// <paramref name="maxRandomExtraDelayMillis"/>.
        /// </remarks>
        /// <param name="maxNumAttempts">Maximum number of attempts, including the initial attempt and all retries.</param>
        /// <param name="baseIntervalMillis">Initial delay in milliseconds before the first retry.</param>
        /// <param name="maxIntervalMillis">
        /// Optional maximum delay in milliseconds. When specified, the backoff delay will not grow beyond this value.
        /// </param>
        /// <param name="maxRandomExtraDelayMillis">
        /// Optional maximum random extra delay in milliseconds to add as jitter to each computed backoff delay.
        /// </param>
        /// <returns>A retry profile configured with exponential backoff.</returns>
        public static RetryProfile ExponentialBackoff(
            uint maxNumAttempts,
            uint baseIntervalMillis,
            uint? maxIntervalMillis = null,
            uint? maxRandomExtraDelayMillis = null
        ) {
            return new ExponentialBackoffImpl(
                maxNumAttempts,
                baseIntervalMillis,
                maxIntervalMillis,
                maxRandomExtraDelayMillis
            );
        }

        /// <summary>
        /// Returns the default <see cref="RetryProfile"/> that uses a uniform delay
        /// of 0.75 seconds between attempts, with a hard limit of 12 attempts.
        /// </summary>
        /// <remarks>
        /// This mirrors the default configuration used by the Android implementation.
        /// </remarks>
        /// <returns>The default retry profile instance.</returns>
        public static readonly RetryProfile Default = Uniform(12u, 750u);

        internal RetryProfile(Type type)
        {
            this.type = type;
        }
    }

    /// <summary>
    /// Collection of retry policies for different SDK operations.
    /// </summary>
    [Serializable]
    internal class RetryPolicies
    {
        /// <summary>
        /// Default retry profile to use when a more specific one is not set.
        /// </summary>
        [CanBeNull] public RetryProfile defaultRetryProfileOverride;

        /// <summary>Retry profile override for querying pending order status.</summary>
        [CanBeNull] public RetryProfile pendingOrderStatusQueryRetryProfileOverride;

        /// <summary>Retry profile override for creating an order.</summary>
        [CanBeNull] public RetryProfile createOrderRetryProfileOverride;

        /// <summary>Retry profile override for authentication.</summary>
        [CanBeNull] public RetryProfile authenticateRetryProfileOverride;

        /// <summary>Retry profile override for querying products.</summary>
        [CanBeNull] public RetryProfile queryProductsRetryProfileOverride;

        /// <summary>Retry profile override for querying purchases.</summary>
        [CanBeNull] public RetryProfile queryPurchasesRetryProfileOverride;

        /// <summary>Retry profile override for consuming purchases.</summary>
        [CanBeNull] public RetryProfile consumePurchasesRetryProfileOverride;

        public class Builder
        {
            public enum RetryProfileType
            {
                Default,
                PendingOrder,
                CreateOrder,
                Authenticate,
                QueryProducts,
                QueryPurchases,
                ConsumePurchases
            } 
            
            private RetryPolicies _policies = new RetryPolicies();
            
            public static Builder Create() => new Builder();
            public static Builder Update(RetryPolicies policies)
            {
                var builder = new Builder();
                if (policies != null)
                    builder._policies = policies;
                return builder;
            }
            
            
            public Builder SetRetryPolicyDefault(RetryProfileType type)
            {
                switch (type)
                {
                    case RetryProfileType.Default: SetDefaultRetryProfile( RetryProfile.Default ); break;
                    case RetryProfileType.PendingOrder: SetPendingOrderStatusQueryRetryProfile( RetryProfile.Default ); break;
                    case RetryProfileType.CreateOrder: SetCreateOrderRetryProfile( RetryProfile.Default ); break;
                    case RetryProfileType.Authenticate: SetAuthenticateRetryProfile( RetryProfile.Default ); break;
                    case RetryProfileType.QueryProducts: SetQueryProductsRetryProfile( RetryProfile.Default ); break;
                    case RetryProfileType.QueryPurchases: SetQueryPurchasesRetryProfile( RetryProfile.Default ); break;
                    case RetryProfileType.ConsumePurchases: SetConsumePurchasesRetryProfile( RetryProfile.Default ); break;
                }
                
                return this;
            }
            
            public Builder SetRetryPolicyUniform(RetryProfileType type, uint maxNumAttempts, uint intervalMillis)
            {
                switch (type)
                {
                    case RetryProfileType.Default: SetDefaultRetryProfile( RetryProfile.Uniform(maxNumAttempts, intervalMillis) ); break;
                    case RetryProfileType.PendingOrder: SetPendingOrderStatusQueryRetryProfile( RetryProfile.Uniform(maxNumAttempts, intervalMillis) ); break;
                    case RetryProfileType.CreateOrder: SetCreateOrderRetryProfile( RetryProfile.Uniform(maxNumAttempts, intervalMillis) ); break;
                    case RetryProfileType.Authenticate: SetAuthenticateRetryProfile( RetryProfile.Uniform(maxNumAttempts, intervalMillis) ); break;
                    case RetryProfileType.QueryProducts: SetQueryProductsRetryProfile( RetryProfile.Uniform(maxNumAttempts, intervalMillis) ); break;
                    case RetryProfileType.QueryPurchases: SetQueryPurchasesRetryProfile( RetryProfile.Uniform(maxNumAttempts, intervalMillis) ); break;
                    case RetryProfileType.ConsumePurchases: SetConsumePurchasesRetryProfile( RetryProfile.Uniform(maxNumAttempts, intervalMillis) ); break;
                }
                
                return this;
            }
            
            public Builder SetRetryPolicyExponentialBackoff(RetryProfileType type, uint maxNumAttempts, uint baseIntervalMillis, uint? maxIntervalMillis = null, uint? maxRandomExtraDelayMillis = null)
            {
                switch (type)
                {
                    case RetryProfileType.Default: SetDefaultRetryProfile( RetryProfile.ExponentialBackoff(maxNumAttempts, baseIntervalMillis, maxIntervalMillis, maxRandomExtraDelayMillis) ); break;
                    case RetryProfileType.PendingOrder: SetPendingOrderStatusQueryRetryProfile( RetryProfile.ExponentialBackoff(maxNumAttempts, baseIntervalMillis, maxIntervalMillis, maxRandomExtraDelayMillis) ); break;
                    case RetryProfileType.CreateOrder: SetCreateOrderRetryProfile( RetryProfile.ExponentialBackoff(maxNumAttempts, baseIntervalMillis, maxIntervalMillis, maxRandomExtraDelayMillis) ); break;
                    case RetryProfileType.Authenticate: SetAuthenticateRetryProfile( RetryProfile.ExponentialBackoff(maxNumAttempts, baseIntervalMillis, maxIntervalMillis, maxRandomExtraDelayMillis) ); break;
                    case RetryProfileType.QueryProducts: SetQueryProductsRetryProfile( RetryProfile.ExponentialBackoff(maxNumAttempts, baseIntervalMillis, maxIntervalMillis, maxRandomExtraDelayMillis) ); break;
                    case RetryProfileType.QueryPurchases: SetQueryPurchasesRetryProfile( RetryProfile.ExponentialBackoff(maxNumAttempts, baseIntervalMillis, maxIntervalMillis, maxRandomExtraDelayMillis) ); break;
                    case RetryProfileType.ConsumePurchases: SetConsumePurchasesRetryProfile( RetryProfile.ExponentialBackoff(maxNumAttempts, baseIntervalMillis, maxIntervalMillis, maxRandomExtraDelayMillis) ); break;
                }
                
                return this;
            }
            
            /// <summary> Sets the default retry profile override. </summary>
            /// <param name="profile"> The retry profile to use as default, or <c>null</c> to clear the override. </param>
            public Builder SetDefaultRetryProfile([CanBeNull] RetryProfile profile) { _policies.defaultRetryProfileOverride = profile; return this; }

            /// <summary> Sets the retry profile override for querying pending order status. </summary>
            /// <param name="profile"> The retry profile to use, or <c>null</c> to fall back to the default profile. </param>
            public Builder SetPendingOrderStatusQueryRetryProfile([CanBeNull] RetryProfile profile)
            {
                _policies.pendingOrderStatusQueryRetryProfileOverride = profile;
                return this;
            }

            /// <summary> Sets the retry profile override for creating an order. </summary>
            /// <param name="profile"> The retry profile to use, or <c>null</c> to fall back to the default profile. </param>
            public Builder SetCreateOrderRetryProfile([CanBeNull] RetryProfile profile)
            {
                _policies.createOrderRetryProfileOverride = profile;
                return this;
            }

            /// <summary> Sets the retry profile override for authentication. </summary>
            /// <param name="profile"> The retry profile to use, or <c>null</c> to fall back to the default profile. </param>
            public Builder SetAuthenticateRetryProfile([CanBeNull] RetryProfile profile)
            {
                _policies.authenticateRetryProfileOverride = profile;
                return this;
            }

            /// <summary> Sets the retry profile override for querying products. </summary>
            /// <param name="profile"> The retry profile to use, or <c>null</c> to fall back to the default profile. </param>
            public Builder SetQueryProductsRetryProfile([CanBeNull] RetryProfile profile)
            {
                _policies.queryProductsRetryProfileOverride = profile;
                return this;
            }

            /// <summary> Sets the retry profile override for querying purchases. </summary>
            /// <param name="profile"> The retry profile to use, or <c>null</c> to fall back to the default profile. </param>
            public Builder SetQueryPurchasesRetryProfile([CanBeNull] RetryProfile profile)
            {
                _policies.queryPurchasesRetryProfileOverride = profile;
                return this;
            }

            /// <summary> Sets the retry profile override for consuming purchases. </summary>
            /// <param name="profile"> The retry profile to use, or <c>null</c> to fall back to the default profile. </param>
            public Builder SetConsumePurchasesRetryProfile([CanBeNull] RetryProfile profile)
            {
                _policies.consumePurchasesRetryProfileOverride = profile;
                return this;
            }

            public RetryPolicies Build() => _policies;
            public static RetryPolicies Empty() => Create().Build();
        }
        
        /// <summary> Serializes to JSON. </summary>
        /// <returns>JSON string representation.</returns>
        public string ToJson() => XsollaClientHelpers.ToJson(this);

        /// <summary> Returns the JSON string representation. </summary>
        public override string ToString() => ToJson();
    }

    /// <summary>
    /// Tunables for the per-SKU product fetch (see <c>.docs/sku-optimization</c>). Unity is the single
    /// authority that governs every platform: the standalone implementation and the native Android SDK
    /// both consume these values; iOS does not support them yet.
    /// <para/>
    /// Each override field is nullable — leave it unset to use the Unity-governed default exposed by the
    /// matching <c>Default*</c> constant. The <c>Effective*</c> accessors resolve a null override to that
    /// default, so callers always read a concrete value.
    /// <para/>
    /// Lives in the Common assembly (like <see cref="RetryPolicies"/>) so the store-client extensions
    /// handler can hand it to the platform implementations; the configurable instance is held by
    /// <c>XsollaStoreClientExtensionsSettings.productFetchSettings</c>.
    /// </summary>
    [Serializable]
    public class ProductFetchSettings
    {
        /// <summary>Max SKUs fetched per network request. Clamped to the backend limit of 50.</summary>
        public const int DefaultMaxItemsPerRequest = 50;

        /// <summary>Max product-fetch requests in flight at once for a single query.</summary>
        public const int DefaultMaxParallelRequests = 4;

        /// <summary>Time-to-live for cached product entries, in milliseconds (1 hour). Native cache only.</summary>
        public const long DefaultCacheTtlMillis = 3_600_000;

        /// <summary>
        /// Max SKUs fetched per network request. Clamped to the backend limit of 50.
        /// <para/>
        /// <c>null</c> = <see cref="DefaultMaxItemsPerRequest"/> (50).
        /// </summary>
        public int? maxItemsPerRequest;

        /// <summary>
        /// Max product-fetch requests in flight at once for a single query.
        /// <c>1</c> = strictly sequential; <c>N</c> = a sliding window of N.
        /// <para/>
        /// <c>null</c> = <see cref="DefaultMaxParallelRequests"/> (4).
        /// </summary>
        public int? maxParallelRequests;

        /// <summary>
        /// Time-to-live for cached product entries, in milliseconds, measured from when the entry was
        /// cached.
        /// <para/>
        /// Android only — the standalone implementation does not cache fetched products, so this has no
        /// effect there.
        /// <para/>
        /// <c>null</c> = <see cref="DefaultCacheTtlMillis"/> (1 hour).
        /// </summary>
        public long? cacheTtlMillis;

        public int EffectiveMaxItemsPerRequest => maxItemsPerRequest ?? DefaultMaxItemsPerRequest;
        public int EffectiveMaxParallelRequests => maxParallelRequests ?? DefaultMaxParallelRequests;
        public long EffectiveCacheTtlMillis => cacheTtlMillis ?? DefaultCacheTtlMillis;

        public ProductFetchSettings SetMaxItemsPerRequest(int? maxItemsPerRequest)
        {
            this.maxItemsPerRequest = maxItemsPerRequest;
            return this;
        }

        public ProductFetchSettings SetMaxParallelRequests(int? maxParallelRequests)
        {
            this.maxParallelRequests = maxParallelRequests;
            return this;
        }

        public ProductFetchSettings SetCacheTtlMillis(long? cacheTtlMillis)
        {
            this.cacheTtlMillis = cacheTtlMillis;
            return this;
        }
    }
}