using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Reactive.Streams;

namespace BiDaFlow.Internal
{
    internal sealed class PublisherAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly IPublisher<T> _source;

        public PublisherAsyncEnumerable(IPublisher<T> source)
        {
            this._source = source;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var enumerator = new PublisherAsyncEnumerator<T>(cancellationToken);
            this._source.Subscribe(enumerator);
            return enumerator;
        }
    }

    internal sealed class PublisherAsyncEnumerator<T> : IAsyncEnumerator<T>, ISubscriber<T>, IValueTaskSource<bool>
    {
        private readonly TaskCompletionSource<ISubscription> _subscription = new TaskCompletionSource<ISubscription>();
        private readonly CancellationToken _cancellationToken;
        private ManualResetValueTaskSourceCore<bool?> _taskHelper;
        private CancellationTokenRegistration _cancellationReg;
        private volatile bool _isCompleted;
        private int _isAwaiting;

        public PublisherAsyncEnumerator(CancellationToken cancellationToken)
        {
            this._cancellationToken = cancellationToken;

            if (cancellationToken.CanBeCanceled)
            {
                _cancellationReg = cancellationToken.Register(HandleCancellation);
            }

            async void HandleCancellation()
            {
                var subscription = await this._subscription.Task.ConfigureAwait(false);

                Exception? exception = null;

                lock (this.SubscriptionLock)
                {
                    if (!this._isCompleted)
                    {
                        this._isCompleted = true;
                        try
                        {
                            subscription.Cancel();
                        }
                        catch (Exception ex)
                        {
                            exception = ex;
                        }
                    }
                }

                if (WriteResult())
                {
                    if (exception == null)
                        this._taskHelper.SetResult(null);
                    else
                        this._taskHelper.SetException(exception);
                }
            }
        }

        /// <summary>
        /// A lock object to avoid to call methods of ISubscription concurrently.
        /// </summary>
        private object SubscriptionLock => this._subscription;

        public T Current { get; private set; } = default!;

        public ValueTask<bool> MoveNextAsync()
        {
            if (this._isAwaiting != 0)
                throw new InvalidOperationException("The previous task has not been completed.");

            this._taskHelper.Reset();

            this._isAwaiting = 1;
            Interlocked.MemoryBarrier();

            if (this._cancellationToken.IsCancellationRequested)
            {
                if (this.WriteResult())
                    this._taskHelper.SetResult(null);
            }
            else if (this._isCompleted)
            {
                if (this.WriteResult())
                    this._taskHelper.SetResult(false);
            }
            else
            {
                Request();
            }

            return new ValueTask<bool>(this, this._taskHelper.Version);

            async void Request()
            {
                try
                {
                    var subscription = await this._subscription.Task.ConfigureAwait(false);

                    lock (this.SubscriptionLock)
                    {
                        if (!this._isCompleted)
                            subscription.Request(1);
                    }
                }
                catch (Exception ex)
                {
                    if (this.WriteResult())
                        this._taskHelper.SetException(ex);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            this._cancellationReg.Dispose();

            var subscription = await this._subscription.Task.ConfigureAwait(false);

            lock (this.SubscriptionLock)
            {
                if (!this._isCompleted)
                {
                    this._isCompleted = true;
                    subscription.Cancel();
                }
            }
        }

        private bool WriteResult()
        {
            return Interlocked.CompareExchange(ref this._isAwaiting, 0, 1) == 1;
        }

        #region ISubscriber<T>

        void ISubscriber<T>.OnSubscribe(ISubscription subscription)
        {
            this._subscription.SetResult(subscription);
        }

        void ISubscriber<T>.OnNext(T element)
        {
            this.Current = element;

            if (this.WriteResult())
                this._taskHelper.SetResult(true);
        }

        void ISubscriber<T>.OnError(Exception cause)
        {
            this._isCompleted = true;

            if (this.WriteResult())
                this._taskHelper.SetException(cause);

            this._cancellationReg.Dispose();
        }

        void ISubscriber<T>.OnComplete()
        {
            this._isCompleted = true;

            if (this.WriteResult())
                this._taskHelper.SetResult(null);

            this._cancellationReg.Dispose();
        }

        #endregion

        #region IValueTaskSource<bool>

        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token)
        {
            var status = this._taskHelper.GetStatus(token);
            return status == ValueTaskSourceStatus.Succeeded && this._taskHelper.GetResult(token) == null
                ? ValueTaskSourceStatus.Canceled
                : status;
        }

        bool IValueTaskSource<bool>.GetResult(short token)
        {
            var result = this._taskHelper.GetResult(token);
            if (result == null) throw new OperationCanceledException(this._cancellationToken);
            return result.Value;
        }

        void IValueTaskSource<bool>.OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            this._taskHelper.OnCompleted(continuation, state, token, flags);
        }

        #endregion
    }
}
