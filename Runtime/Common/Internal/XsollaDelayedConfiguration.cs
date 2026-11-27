using System;
using JetBrains.Annotations;
using Xsolla.SDK.Utils;

namespace Xsolla.SDK.Common
{
    /// <summary>
    /// Resolves a configuration whose delayed configuration task maps it, for <c>XsollaStoreClient</c>,
    /// <c>XsollaPurchasingCore</c> and <c>XsollaLoginClient</c> alike.
    /// <para/>
    /// A delayed configuration task that faults or is cancelled, or a mapper that throws or returns null, fails the
    /// settings future with a "configuration failed: " error instead of leaving it waiting forever.
    /// </summary>
    internal static class XsollaDelayedConfiguration
    {
        /// <summary>
        /// Completes <paramref name="promise"/> with the configuration the mapper maps once the delayed configuration
        /// task of <paramref name="configuration"/> completes, or with an error.
        /// <para/>
        /// A faulted or cancelled task is caught here rather than escaping this async void. The mapper runs through
        /// <paramref name="runOnStartThread"/>, where <see cref="Complete"/> catches its throw too, even when that run
        /// is queued for a later frame.
        /// </summary>
        /// <param name="configuration">Client configuration with a delayed configuration task.</param>
        /// <param name="runOnStartThread">Runs the mapper on the start thread.</param>
        /// <param name="promise">Promise of the settings future the owner waits on.</param>
        /// <param name="tag">Log tag of the owner, under which a failure is logged.</param>
        public static async void Resolve(
            XsollaClientConfiguration configuration,
            Action<Action> runOnStartThread,
            ISimplePromise<XsollaClientConfiguration, string> promise,
            string tag
        )
        {
            XsollaClientConfiguration.OnDelayedConfiguration mapper = null;
            Exception failure = null;
            try
            {
                mapper = await configuration.delayedTask;
            }
            catch (Exception e)
            {
                failure = e;
            }

            runOnStartThread(() => Complete(configuration, mapper, failure, promise, tag));
        }

        /// <summary>
        /// Completes the settings future with the configuration the mapper maps, or with an error when the delayed
        /// configuration task failed or the mapper throws or returns null.
        /// <para/>
        /// Only the mapper is guarded: a callback the completion runs that throws must not fail the future a second
        /// time, after some callbacks already took its success. The app's task or mapper failing is also passed to Unity
        /// as an exception whatever the SDK log level, as it was when it escaped.
        /// </summary>
        private static void Complete(
            XsollaClientConfiguration configuration,
            [CanBeNull] XsollaClientConfiguration.OnDelayedConfiguration mapper,
            [CanBeNull] Exception failure,
            ISimplePromise<XsollaClientConfiguration, string> promise,
            string tag
        )
        {
            XsollaClientConfiguration resolved = null;
            if (failure == null)
            {
                try
                {
                    resolved = mapper(configuration);
                }
                catch (Exception e)
                {
                    failure = e;
                }
            }

            if (failure != null)
            {
                XsollaLogger.Error(tag, $"Configuration failed: {failure}");
                UnityEngine.Debug.LogException(failure);
                promise.CompleteWithError($"configuration failed: {failure.Message}");
            }
            else if (resolved == null)
            {
                XsollaLogger.Error(tag, "Configuration failed: the delayed configuration returned null");
                promise.CompleteWithError("configuration failed: the delayed configuration returned null");
            }
            else
            {
                promise.Complete(resolved);
            }
        }
    }
}
