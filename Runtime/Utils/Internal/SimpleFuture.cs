using System;
using System.Collections.Generic;

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

            void OnFinish()
            {
                if (f1.isCompleted && f2.isCompleted)
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

    internal class SimpleFutureImpl<T, E> : ISimpleFuture<T, E>, ISimplePromise<T, E>
    {
        private T value;
        private E error;
        
        public bool isCompleted { get; private set; }
        public bool isFailed { get; private set; }
        
        private static readonly Action<T> noOpSuccess = _ => { };
        private static readonly Action<E> noOpError = _ => { };

        private List<(Action<T> onSuccess, Action<E> onError)> actions = new List<(Action<T> onSuccess, Action<E> onError)>();
        
        public ISimplePromise<T, E> Promise => this;
        
        public void Complete(T value)
        {
            isCompleted = true;
            isFailed = false;
            this.value = value;

            foreach (var action in actions)
            {
                action.onSuccess?.Invoke(value);
            }
            actions.Clear();
        }
        
        public void CompleteWithError(E error)
        {
            isCompleted = true;
            isFailed = true;
            this.error = error;

            foreach (var action in actions)
            {
                action.onError?.Invoke(error);
            }
            actions.Clear();
        }
        
        public void OnComplete(Action<T> onSuccess, Action<E> onError)
        {
            if (isCompleted && !isFailed)
            {
                onSuccess?.Invoke(value);
            }
            else if (isCompleted && isFailed)
            {
                onError?.Invoke(error);
            }
            else if (onSuccess != null || onError != null)
            {
                actions.Add((onSuccess ?? noOpSuccess, onError ?? noOpError));
            }
        }

        public bool TryGetValue(out T value)
        {
            value = this.value;
            return isCompleted && !isFailed;
        }

        public bool TryGetError(out E error)
        {
            error = this.error;
            return isCompleted && isFailed;
        }
    }
}