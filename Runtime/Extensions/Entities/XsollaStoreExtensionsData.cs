using System;
using JetBrains.Annotations;
using Xsolla.SDK.Common;
using Xsolla.SDK.Common.Extensions;

namespace Xsolla.SDK.Extensions
{
    public enum XsollaStoreClientEventTypes
    {
        PaystationOpen,
        PaystationLoaded,
        PaystationCancelled,
        PaystationCompleted
    }
    
    /// <summary>
    /// Configuration options for the Xsolla Store SDK extensions.
    /// Use <see cref="Builder"/> to construct and optionally apply settings to the SDK.
    /// </summary>
    [Serializable]
    public class XsollaStoreClientExtensionsSettings
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

        public class Builder
        {
            private XsollaStoreClientExtensionsSettings _settings = new XsollaStoreClientExtensionsSettings();

            public static Builder Create() => new Builder();

            /// <summary>
            /// Creates a builder initialized with an existing settings instance for modification.
            /// </summary>
            /// <param name="settings">The settings instance to modify.</param>
            public static Builder Update(XsollaStoreClientExtensionsSettings settings)
            {
                var builder = new Builder();
                builder._settings = settings;
                return builder;
            }

            private RetryPolicies.Builder.RetryProfileType ConvertType(RetryProfileType type)
            {
                switch (type)
                {
                    case RetryProfileType.Default: return RetryPolicies.Builder.RetryProfileType.Default;
                    case RetryProfileType.PendingOrder: return RetryPolicies.Builder.RetryProfileType.PendingOrder;
                    case RetryProfileType.CreateOrder: return RetryPolicies.Builder.RetryProfileType.CreateOrder;
                    case RetryProfileType.Authenticate: return RetryPolicies.Builder.RetryProfileType.Authenticate;
                    case RetryProfileType.QueryProducts: return RetryPolicies.Builder.RetryProfileType.QueryProducts;
                    case RetryProfileType.QueryPurchases: return RetryPolicies.Builder.RetryProfileType.QueryPurchases;
                    case RetryProfileType.ConsumePurchases: return RetryPolicies.Builder.RetryProfileType.ConsumePurchases;
                }
                
                return RetryPolicies.Builder.RetryProfileType.Default;
            }
            
            public Builder SetRetryPolicyDefault(RetryProfileType type)
            {
                _settings.retryPolicies = RetryPolicies.Builder.Update(_settings.retryPolicies)
                    .SetRetryPolicyDefault(ConvertType(type))
                    .Build();
                return this;
            }
            
            public Builder SetRetryPolicyUniform(RetryProfileType type, uint maxNumAttempts, uint intervalMillis)
            {
                _settings.retryPolicies = RetryPolicies.Builder.Update(_settings.retryPolicies)
                    .SetRetryPolicyUniform(ConvertType(type), maxNumAttempts,intervalMillis)
                    .Build();
                return this;
            }
            public Builder SetRetryPolicyExponentialBackoff(RetryProfileType type, uint maxNumAttempts, uint baseIntervalMillis, uint? maxIntervalMillis = null, uint? maxRandomExtraDelayMillis = null)
            {
                _settings.retryPolicies = RetryPolicies.Builder.Update(_settings.retryPolicies)
                    .SetRetryPolicyExponentialBackoff(ConvertType(type), maxNumAttempts, baseIntervalMillis, maxIntervalMillis, maxRandomExtraDelayMillis)
                    .Build();
                return this;
            }
            
            public Builder SetEventsCallback(Action<XsollaStoreClientEventTypes, object> callback)
            {
                _settings.eventsCallback = callback;
                return this;
            }

            /// <summary>
            /// Sets the per-SKU product fetch tunables. Standalone and Android only; iOS does not support
            /// these yet. Pass <c>null</c> to keep the Unity-governed defaults (see
            /// <see cref="ProductFetchSettings"/>).
            /// </summary>
            public Builder SetProductFetchSettings([CanBeNull] ProductFetchSettings settings)
            {
                _settings.productFetchSettings = settings;
                return this;
            }

            public XsollaStoreClientExtensionsSettings Build() => _settings;
            public static XsollaStoreClientExtensionsSettings Empty() => Create().Build();
        }

        [CanBeNull] internal RetryPolicies retryPolicies;
        [CanBeNull] internal Action<XsollaStoreClientEventTypes, object> eventsCallback;
        [CanBeNull] internal ProductFetchSettings productFetchSettings;

        /// <summary> Serializes to JSON. </summary>
        /// <returns>JSON string representation.</returns>
        public string ToJson() => XsollaClientHelpers.ToJson(this);

        /// <summary> Returns the JSON string representation. </summary>
        public override string ToString() => ToJson();
    }
}