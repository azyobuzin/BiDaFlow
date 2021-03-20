using System;
using System.Collections.Generic;
using System.Threading;
using Reactive.Streams;

namespace BiDaFlow.Internal
{
    internal sealed class AsyncEnumerablePublisher<T> : IPublisher<T>
    {
        private readonly IAsyncEnumerable<T> _source;

        public AsyncEnumerablePublisher(IAsyncEnumerable<T> source)
        {
            this._source = source;
        }

        public void Subscribe(ISubscriber<T> subscriber)
        {
            if (subscriber == null) throw new ArgumentNullException(nameof(subscriber));

            _ = new AsyncEnumerableSubscription<T>(this._source, subscriber);
        }
    }

    internal sealed class AsyncEnumerableSubscription<T> : ISubscription
    {
        private readonly IAsyncEnumerator<T>? _enumerator;
        private readonly ISubscriber<T> _subscriber;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _isCompleted;
        private long _requested;
        private bool _workerRunning;

        public AsyncEnumerableSubscription(IAsyncEnumerable<T> source, ISubscriber<T> subscriber)
        {
            this._enumerator = source.GetAsyncEnumerator(this._cts.Token);
            this._subscriber = subscriber;

            subscriber.OnSubscribe(this);

            if (this._enumerator == null)
            {
                this._isCompleted = true;
                subscriber.OnComplete();
            }
        }

        private object Lock => this._cts;

        public void Request(long n)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), "n cannot be negative.");
            if (n == 0) return;

            var startWorker = false;

            lock (this.Lock)
            {
                if (this._isCompleted) return;

                var current = this._requested;
                var newVal = unchecked(current + n);
                if (newVal < current) newVal = long.MaxValue;
                this._requested = newVal;

                if (!this._workerRunning)
                {
                    this._workerRunning = true;
                    startWorker = true;
                }
            }

            if (startWorker) this.StartWorker();
        }

        public void Cancel()
        {
            this._cts.Cancel();

            var startWorker = false;

            lock (this.Lock)
            {
                // Wake the worker to dispose the enumerator
                if (!this._isCompleted && !this._workerRunning)
                {
                    this._workerRunning = true;
                    startWorker = true;
                }
            }

            if (startWorker) this.StartWorker();
        }

        private void StartWorker()
        {
            ThreadPool.QueueUserWorkItem(WorkerProc, this);

            static async void WorkerProc(object state)
            {
                var self = (AsyncEnumerableSubscription<T>)state;
                var enumerator = self._enumerator ?? throw new InvalidOperationException();
                Exception? exception = null;

                try
                {
                    while (!self._isCompleted && !self._cts.IsCancellationRequested)
                    {
                        lock (self.Lock)
                        {
                            if (self._requested == 0)
                            {
                                self._workerRunning = false;
                                return;
                            }

                            self._requested--;
                        }

                        var hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);

                        if (!hasNext || self._cts.IsCancellationRequested)
                            break;

                        self._subscriber.OnNext(enumerator.Current);
                    }
                }
                catch (Exception ex)
                {
                    exception = ex;
                }

                if (!self._isCompleted)
                {
                    self._isCompleted = true;

                    try
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        exception = exception == null ? ex : new AggregateException(exception, ex);
                    }

                    if (!self._cts.IsCancellationRequested)
                    {
                        if (exception != null)
                            self._subscriber.OnError(exception);
                        else
                            self._subscriber.OnComplete();
                    }
                }

                self._workerRunning = false;
            }
        }
    }
}
