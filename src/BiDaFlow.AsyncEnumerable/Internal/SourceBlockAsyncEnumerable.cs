using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks.Sources;

namespace BiDaFlow.Internal
{
    /// <seealso cref="Fluent.DataflowAsyncEnumerable.AsAsyncEnumerable{T}(ISourceBlock{T})"/>
    internal sealed class SourceBlockAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly ISourceBlock<T> _source;

        public SourceBlockAsyncEnumerable(ISourceBlock<T> source)
        {
            this._source = source;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new SourceBlockAsyncEnumerator<T>(this._source, cancellationToken);
        }
    }

    internal sealed class SourceBlockAsyncEnumerator<T> : IAsyncEnumerator<T>, ITargetBlock<T>, IValueTaskSource<bool>
    {
        private readonly ISourceBlock<T> _source;
        private CancellationToken _cancellationToken;
        private ManualResetValueTaskSourceCore<bool?> _taskHelper;
        private readonly IDisposable _unlinker;
        private CancellationTokenRegistration _cancellationReg;
        private bool _isCompleted;
        private Exception? _exception;
        private bool _isAwaiting;
        private readonly Queue<DataflowMessageHeader> _queue = new Queue<DataflowMessageHeader>();

        public SourceBlockAsyncEnumerator(ISourceBlock<T> source, CancellationToken cancellationToken)
        {
            this._source = source;
            this._unlinker = source.LinkTo(this, new DataflowLinkOptions() { PropagateCompletion = true });
            this._cancellationToken = cancellationToken;

            this._taskHelper.RunContinuationsAsynchronously = true;

            if (cancellationToken.CanBeCanceled)
            {
                this._cancellationReg = cancellationToken.Register(state => ((IDataflowBlock)state).Complete(), this);
            }
        }

        private object Lock => this._queue; // any readonly object

        public T Current { get; private set; } = default!;

        public ValueTask<bool> MoveNextAsync()
        {
            lock (this.Lock)
            {
                this._taskHelper.Reset();

                if (this._exception != null)
                {
                    this._taskHelper.SetException(this._exception);
                }
                else if (this._cancellationToken.IsCancellationRequested)
                {
                    this._taskHelper.SetResult(null);
                }
                else if (this._isCompleted)
                {
                    this._taskHelper.SetResult(false);
                }
                else
                {
                    this._isAwaiting = true;

                    // Dequeue postponed message
                    while (this._queue.Count > 0)
                    {
                        var header = this._queue.Dequeue();
                        var consumedValue = this._source.ConsumeMessage(header, this, out var consumed);

                        if (consumed)
                        {
                            this.Current = consumedValue;
                            this._isAwaiting = false;
                            this._taskHelper.SetResult(true);
                            break;
                        }
                    }
                }

                return new ValueTask<bool>(this, this._taskHelper.Version);
            }
        }

        public ValueTask DisposeAsync()
        {
            this._unlinker?.Dispose();
            ((IDataflowBlock)this).Complete();
            return default;
        }

        #region ITargetBlock<T>

        Task IDataflowBlock.Completion => throw new NotImplementedException();

        DataflowMessageStatus ITargetBlock<T>.OfferMessage(DataflowMessageHeader messageHeader, T messageValue, ISourceBlock<T> source, bool consumeToAccept)
        {
            if (source != null && source != this._source) throw new ArgumentException("Unexpected source.");
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (consumeToAccept && source == null) throw new ArgumentException("source is null though consumeToAccept is true.");

            lock (this.Lock)
            {
                if (this._isCompleted) return DataflowMessageStatus.DecliningPermanently;

                if (this._isAwaiting)
                {
                    if (source != null && consumeToAccept)
                    {
                        messageValue = source.ConsumeMessage(messageHeader, this, out var consumed);
                        if (!consumed) return DataflowMessageStatus.NotAvailable;
                    }

                    this.Current = messageValue;
                    this._isAwaiting = false;
                    this._taskHelper.SetResult(true);

                    return DataflowMessageStatus.Accepted;
                }

                if (source == null) return DataflowMessageStatus.Declined;

                if (!this._queue.Contains(messageHeader))
                {
                    this._queue.Enqueue(messageHeader);
                }

                return DataflowMessageStatus.Postponed;
            }
        }

        void IDataflowBlock.Complete()
        {
            lock (this.Lock)
            {
                this._isCompleted = true;
                this._cancellationReg.Dispose();

                if (this._isAwaiting)
                {
                    this._isAwaiting = false;
                    this._taskHelper.SetResult(this._cancellationToken.IsCancellationRequested ? (bool?)null : false);
                }

                this.ReleasePostponedMessages();
            }
        }

        void IDataflowBlock.Fault(Exception exception)
        {
            if (exception is AggregateException aex && aex.InnerExceptions.Count == 1)
                exception = aex.InnerException;

            lock (this.Lock)
            {
                this._isCompleted = true;
                this._exception = exception;
                this._cancellationReg.Dispose();

                if (this._isAwaiting)
                {
                    this._isAwaiting = false;
                    this._taskHelper.SetException(exception);
                }

                this.ReleasePostponedMessages();
            }
        }

        private void ReleasePostponedMessages()
        {
            Debug.Assert(Monitor.IsEntered(this.Lock));

            // https://github.com/dotnet/runtime/blob/89b8591928bcb9f90956c938fcd9fcfb2fdfb476/src/libraries/System.Threading.Tasks.Dataflow/src/Internal/Common.cs#L508-L511
            while (this._queue.Count > 0)
            {
                var msg = this._queue.Dequeue();
                if (this._source.ReserveMessage(msg, this))
                    this._source.ReleaseReservation(msg, this);
            }
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
