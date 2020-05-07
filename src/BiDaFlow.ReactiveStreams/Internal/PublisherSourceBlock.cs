using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Reactive.Streams;

namespace BiDaFlow.Internal
{
    internal sealed class PublisherSourceBlock<T> : ISourceBlock<T>, ISubscriber<T>
    {
        private readonly TaskScheduler? _taskScheduler;
        private CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<ValueTuple> _tcs = new TaskCompletionSource<ValueTuple>();
        private readonly LinkManager<T> _linkManager = new LinkManager<T>();
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly AsyncAutoResetEvent _resetEvent;
        private ISubscription? _upstream;
        private int _freeCount;
        private bool _upstreamCanceled;
        private bool _completeRequested;
        private readonly List<Exception> _exceptions = new List<Exception>();

        public PublisherSourceBlock(int prefetch, TaskScheduler? taskScheduler, CancellationToken cancellationToken)
        {
            this._taskScheduler = taskScheduler;
            this._cancellationToken = cancellationToken;
            this._resetEvent = new AsyncAutoResetEvent(true);
            this._freeCount = prefetch;
        }

        #region IDataflowBlock

        public Task Completion => this._tcs.Task;

        public void Complete()
        {
            this._completeRequested = true;
            this._resetEvent.Set();
        }

        public void Fault(Exception exception)
        {
            lock (this._exceptions)
                this._exceptions.Add(exception);

            this.Complete();
        }

        #endregion

        #region ISourceBlock<T>

        IDisposable ISourceBlock<T>.LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions)
        {
            throw new NotImplementedException();
        }

        T ISourceBlock<T>.ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
        {
            throw new NotImplementedException();
        }

        bool ISourceBlock<T>.ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            throw new NotImplementedException();
        }

        void ISourceBlock<T>.ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region ISubscriber<T>

        void ISubscriber<T>.OnSubscribe(ISubscription subscription)
        {
            if (this._upstream != null)
                throw new InvalidOperationException();

            this._upstream = subscription;

            if (this._cancellationToken.IsCancellationRequested)
            {
                try
                {
                    this._upstreamCanceled = true;
                    subscription.Cancel();
                    this._tcs.TrySetCanceled(this._cancellationToken);
                }
                catch (Exception ex)
                {
                    lock (this._exceptions)
                    {
                        this._exceptions.Add(ex);
                        this._tcs.TrySetException(this._exceptions);
                    }
                }

                return;
            }

            // Start worker
            if (this._taskScheduler == null || this._taskScheduler == TaskScheduler.Default)
            {
                ThreadPool.QueueUserWorkItem(state => ((PublisherSourceBlock<T>)state).WorkerProc(), this);
            }
            else
            {
                new TaskFactory(this._taskScheduler).StartNew(this.WorkerProc);
            }

            this.Request();
        }

        void ISubscriber<T>.OnNext(T element)
        {
            this._queue.Enqueue(element);
        }

        void ISubscriber<T>.OnComplete()
        {
            this._upstreamCanceled = true;
            this.Complete();
        }

        void ISubscriber<T>.OnError(Exception cause)
        {
            this._upstreamCanceled = true;
            this.Fault(cause);
        }

        #endregion

        private void Request()
        {
            var n = Interlocked.Exchange(ref this._freeCount, 0);
            Debug.Assert(n >= 0);

            if (n > 0) this._upstream!.Request(n);
        }

        private async void WorkerProc()
        {
            Debug.Assert(this._upstream != null);

            // Do not use ConfigureAwait(false) to respect taskScheduler

            while (!(this._completeRequested || this._cancellationToken.IsCancellationRequested))
            {
                await this._resetEvent.WaitAsync();

                // TOOD: offer
            }

            if (!this._upstreamCanceled)
                this._upstream!.Cancel();
        }
    }
}
