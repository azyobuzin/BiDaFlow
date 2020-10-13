using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Blocks
{
    /// <summary>
    /// Provides a dataflow block that drops input items.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// var sourceBlock = new BufferBlock<int>();
    /// for (int i = 1; i <= 5; i++)
    ///     sourceBlock.Post(i);
    /// sourceBlock.Complete();
    /// 
    /// // Drops even values
    /// sourceBlock.LinkTo(new DropBlock<int>(), x => x % 2 == 0);
    /// 
    /// await sourceBlock
    ///     .RunWith(new ActionBlock<int>(x => Console.WriteLine(x)))
    ///     .Completion;
    /// ]]></code>
    /// </example>
    public class DropBlock<TInput> : ITargetBlock<TInput>
    {
        private readonly TaskCompletionSource<ValueTuple> _tcs = new TaskCompletionSource<ValueTuple>();

        public DropBlock() { }

        public DropBlock(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                this._tcs.TrySetCanceled(cancellationToken);
            }
            else if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => this._tcs.TrySetCanceled(cancellationToken));
            }
        }

        /// <inheritdoc/>
        public Task Completion => this._tcs.Task;

        /// <inheritdoc/>
        public void Complete()
        {
            this._tcs.TrySetResult(default);
        }

        void IDataflowBlock.Fault(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            this._tcs.TrySetException(exception);
        }

        DataflowMessageStatus ITargetBlock<TInput>.OfferMessage(DataflowMessageHeader messageHeader, TInput messageValue, ISourceBlock<TInput>? source, bool consumeToAccept)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (consumeToAccept && source == null) throw new ArgumentException("source is null though consumeToAccept is true.");

            if (this.Completion.IsCompleted) return DataflowMessageStatus.DecliningPermanently;

            if (source != null && consumeToAccept)
            {
                source.ConsumeMessage(messageHeader, this, out var messageConsumed);
                if (!messageConsumed) return DataflowMessageStatus.NotAvailable;
            }

            return DataflowMessageStatus.Accepted;
        }
    }
}
