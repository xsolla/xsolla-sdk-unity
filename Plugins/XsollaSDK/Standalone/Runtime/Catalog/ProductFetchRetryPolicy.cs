using System;

namespace Xsolla.Core
{
	/// <summary>
	/// Resolved per-chunk retry schedule for the SKU-filtered product fetch in <see cref="Xsolla.Catalog.XsollaCatalog"/>.
	/// <para/>
	/// The standalone catalog lives in a base assembly that cannot see the retry-policy types in the
	/// extensions/common assemblies, so the store layer resolves the configured profile down to this plain
	/// descriptor (attempt count + per-attempt delay) and passes it in. The default mirrors the shared
	/// <c>RetryProfile.Default</c> (uniform 0.75s, 12 attempts) documented to match the Android implementation.
	/// </summary>
	internal sealed class ProductFetchRetryPolicy
	{
		/// <summary>Total attempts, including the initial try (1 = no retries).</summary>
		public readonly int MaxAttempts;

		private readonly Func<int, float> _delaySeconds;

		public ProductFetchRetryPolicy(int maxAttempts, Func<int, float> delaySeconds)
		{
			MaxAttempts = Math.Max(1, maxAttempts);
			_delaySeconds = delaySeconds ?? (_ => 0f);
		}

		/// <summary>Delay before the retry identified by <paramref name="retryIndex"/> (0 = first retry).</summary>
		public float DelaySeconds(int retryIndex) => Math.Max(0f, _delaySeconds(retryIndex));

		public static ProductFetchRetryPolicy Default => new ProductFetchRetryPolicy(12, _ => 0.75f);
	}
}
