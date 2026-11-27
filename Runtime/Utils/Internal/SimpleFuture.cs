using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Xsolla.SDK.Utils
{
    /// <summary>
    /// SimplePromise interface
    /// </summary>
    internal interface ISimplePromise<in T, in E>
    {
        void Complete(T value);
        void CompleteWithError(E error);
    }
    
    /// <summary>
    /// SimplePromise interface
    /// </summary>
    internal interface ISimpleFuture<T, E>
    {
        void OnComplete(Action<T> onSuccess, Action<E> onError);
        bool TryGetValue(out T value);
        bool TryGetError(out E error);
        
        bool isCompleted { get;}
        bool isFailed { get;}

        ISimplePromise<T, E> Promise { get;}
    }

    /// <summary>
    /// SimpleFuture
    /// </summary>
    internal static class SimpleFuture
    {
        public static ISimpleFuture<T, E> Create<T, E>(out ISimplePromise<T, E> promise)
        {
            var future = new SimpleFutureImpl<T, E>();
            promise = future;
            return future;
        }
        
        
        public static ISimpleFuture<T3, E> Zip<T1, T2, T3, E>( this ISimpleFuture<T1, E> f1, ISimpleFuture<T2, E> f2, Func<T1, T2, T3> zipper)
        {
            var future = Create<T3, E>(out var promise);
            var finished = 0;

            // Both callbacks see both futures completed when the futures complete on different threads, or when both
            // were already completed at registration; only the first may zip.
            void OnFinish()
            {
                if (f1.isCompleted && f2.isCompleted && Interlocked.CompareExchange(ref finished, 1, 0) == 0)
                {
                    if (f1.isFailed)
                        promise.CompleteWithError(f1.TryGetError(out var error) ? error : default);
                    else if (f2.isFailed)
                        promise.CompleteWithError(f2.TryGetError(out var error) ? error : default);
                    else
                        promise.Complete(zipper(f1.TryGetValue(out var v1) ? v1 : default, f2.TryGetValue(out var v2) ? v2 : default));
                }
            }
            
            f1.OnComplete(onSuccess: _ => OnFinish(), onError: _ => OnFinish());
            f2.OnComplete(onSuccess: _ => OnFinish(), onError: _ => OnFinish());
            
            return future;
        }
    }

    /// <summary>
    /// A future that is its own promise. Completing it runs every callback registered so far, in registration order,
    /// also when one of them throws: the first exception propagates with its original stack once all of them have run,
    /// and any later one is only logged.
    /// <para/>
    /// Safe to register on and complete from any thread: a callback registered while the future completes runs exactly
    /// once, either with the completion or immediately on registration. Callbacks run outside the lock, so they may
    /// register on this future or wait on threads that do.
    /// </summary>
    internal class SimpleFutureImpl<T, E> : ISimpleFuture<T, E>, ISimplePromise<T, E>
    {
        private T value;
        private E error;
        private bool completed;
        private bool failed;
        
        public bool isCompleted
        {
            get
            {
                lock (gate)
                    return completed;
            }
        }

        public bool isFailed
        {
            get
            {
                lock (gate)
                    return failed;
            }
        }
        
        private static readonly Action<T> noOpSuccess = _ => { };
        private static readonly Action<E> noOpError = _ => { };

        /// <summary>
        /// Guards <see cref="actions"/> and the completion state, so registration and completion each see the other
        /// either entirely or not at all.
        /// </summary>
        private readonly object gate = new object();

        private List<(Action<T> onSuccess, Action<E> onError)> actions = new List<(Action<T> onSuccess, Action<E> onError)>();
        
        public ISimplePromise<T, E> Promise => this;
        
        public void Complete(T value)
        {
            List<(Action<T> onSuccess, Action<E> onError)> pending;
            lock (gate)
            {
                completed = true;
                failed = false;
                this.value = value;
                pending = actions;
                actions = new List<(Action<T> onSuccess, Action<E> onError)>();
            }

            RunCallbacks(pending, action => action.onSuccess?.Invoke(value));
        }
        
        public void CompleteWithError(E error)
        {
            List<(Action<T> onSuccess, Action<E> onError)> pending;
            lock (gate)
            {
                completed = true;
                failed = true;
                this.error = error;
                pending = actions;
                actions = new List<(Action<T> onSuccess, Action<E> onError)>();
            }

            RunCallbacks(pending, action => action.onError?.Invoke(error));
        }

        /// <summary>
        /// Hands every callback in <paramref name="pending"/> to <paramref name="invoke"/>, also when one throws, then
        /// rethrows the first exception; a later one is reported to Unity as an exception instead.
        /// <para/>
        /// One failing callback must not cost the others their result, such as purchase deliveries held until the
        /// catalog arrives, while its exception still reaches whoever completed the future. A later one, most likely
        /// thrown by the app's own code, is reported whatever the SDK log level, as it would be had it escaped.
        /// </summary>
        private static void RunCallbacks(
            List<(Action<T> onSuccess, Action<E> onError)> pending,
            Action<(Action<T> onSuccess, Action<E> onError)> invoke
        )
        {
            ExceptionDispatchInfo firstException = null;
            foreach (var action in pending)
            {
                try
                {
                    invoke(action);
                }
                catch (Exception exception)
                {
                    if (firstException == null)
                        firstException = ExceptionDispatchInfo.Capture(exception);
                    else
                        UnityEngine.Debug.LogException(exception);
                }
            }

            firstException?.Throw();
        }
        
        public void OnComplete(Action<T> onSuccess, Action<E> onError)
        {
            bool wasCompleted;
            bool wasFailed;
            T completedValue;
            E completedError;
            lock (gate)
            {
                wasCompleted = completed;
                wasFailed = failed;
                completedValue = value;
                completedError = error;
                if (!wasCompleted && (onSuccess != null || onError != null))
                    actions.Add((onSuccess ?? noOpSuccess, onError ?? noOpError));
            }

            if (wasCompleted && !wasFailed)
            {
                onSuccess?.Invoke(completedValue);
            }
            else if (wasCompleted && wasFailed)
            {
                onError?.Invoke(completedError);
            }
        }

        public bool TryGetValue(out T value)
        {
            lock (gate)
            {
                value = this.value;
                return completed && !failed;
            }
        }

        public bool TryGetError(out E error)
        {
            lock (gate)
            {
                error = this.error;
                return completed && failed;
            }
        }
    }
}