using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Internal;

namespace BiDaFlow.Blocks
{
    internal sealed class EnumerableSourceBlock<T> : IReceivableSourceBlock<T>
    {
        private readonly EnumerableSourceCore<T> _core;
        private readonly IEnumerable<T>? _enumerable;
        private readonly CancellationToken _cancellationToken;
        private IEnumerator<T>? _enumerator;

        public EnumerableSourceBlock(IEnumerable<T> enumerable, TaskScheduler? taskScheduler, CancellationToken cancellationToken)
        {
            this._core = new EnumerableSourceCore<T>(this, this.Enumerate, taskScheduler, cancellationToken);
            this._enumerable = enumerable;
            this._cancellationToken = cancellationToken;
        }

        public EnumerableSourceBlock(IEnumerator<T> enumerator, TaskScheduler? taskScheduler, CancellationToken cancellationToken)
        {
            this._core = new EnumerableSourceCore<T>(this, this.Enumerate, taskScheduler, cancellationToken);
            this._enumerator = enumerator;
            this._cancellationToken = cancellationToken;
        }

        private void Enumerate()
        {
            try
            {
                if (this._enumerator == null)
                {
                    if (this._core.CompleteRequested ||
                        (this._enumerator = this._enumerable!.GetEnumerator()) == null)
                    {
                        this._core.Complete(true);
                        return;
                    }
                }

                if (!this._core.CompleteRequested && this._enumerator.MoveNext())
                {
                    this._core.OfferItem(this._enumerator.Current);
                }
                else
                {
                    Cleanup();
                }
            }
            catch (Exception ex)
            {
                var canceled = ex is OperationCanceledException && this._cancellationToken.IsCancellationRequested;
                if (!canceled) this._core.AddException(ex);

                Cleanup();
            }

            void Cleanup()
            {
                try
                {
                    this._enumerator?.Dispose();
                }
                catch (Exception disposeException)
                {
                    this._core.AddException(disposeException);
                }

                this._core.Complete(true);
            }
        }

        Task IDataflowBlock.Completion => this._core.Completion;

        void IDataflowBlock.Complete()
            => this._core.Complete(false);

        void IDataflowBlock.Fault(Exception exception)
            => this._core.Fault(exception);

        IDisposable ISourceBlock<T>.LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions)
            => this._core.LinkTo(target, linkOptions);

        T ISourceBlock<T>.ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
            => this._core.ConsumeMessage(messageHeader, target, out messageConsumed);

        bool ISourceBlock<T>.ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
            => this._core.ReserveMessage(messageHeader, target);

        void ISourceBlock<T>.ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
            => this._core.ReserveMessage(messageHeader, target);

        bool IReceivableSourceBlock<T>.TryReceive(Predicate<T> filter, out T item)
            => this._core.TryReceive(filter, out item);

        bool IReceivableSourceBlock<T>.TryReceiveAll(out IList<T> items)
            => this._core.TryReceiveAll(out items);
    }
}
