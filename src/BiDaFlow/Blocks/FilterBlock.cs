using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Fluent;

namespace BiDaFlow.Blocks
{
    /// <summary>
    /// Provides a dataflow block that drops items if the specified function returns <see langword="false"/>.
    /// </summary>
    public class FilterBlock<T> : IPropagatorBlock<T, T>, IReceivableSourceBlock<T>
    {
        private readonly TransformBlock<T, (T, bool)> _inputBlock;
        private readonly TransformWithoutBufferBlock<(T, bool), T> _outputBlock;

        /// <summary>
        /// Initializes a new <see cref="FilterBlock{T}"/>.
        /// </summary>
        /// <param name="predicate">
        /// The function that will be called for each item.
        /// If it returns <see langword="false"/>, the item will be dropped.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="predicate"/> is <see langword="null"/>.
        /// </exception>
        public FilterBlock(Func<T, bool> predicate)
        {
            this._inputBlock = new TransformBlock<T, (T, bool)>(CreateTransform(predicate));
            this._outputBlock = new TransformWithoutBufferBlock<(T, bool), T>(TakeValue);
            this.Initialize();
        }

        /// <summary>
        /// Initializes a new <see cref="FilterBlock{T}"/>.
        /// </summary>
        /// <param name="predicate">
        /// The function that will be called for each item.
        /// If it returns <see langword="false"/>, the item will be dropped.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="predicate"/> is <see langword="null"/>.
        /// </exception>
        public FilterBlock(Func<T, Task<bool>> predicate)
        {
            this._inputBlock = new TransformBlock<T, (T, bool)>(CreateTransform(predicate));
            this._outputBlock = new TransformWithoutBufferBlock<(T, bool), T>(TakeValue);
            this.Initialize();
        }

        /// <summary>
        /// Initializes a new <see cref="FilterBlock{T}"/>.
        /// </summary>
        /// <param name="predicate">
        /// The function that will be called for each item.
        /// If it returns <see langword="false"/>, the item will be dropped.
        /// </param>
        /// <param name="dataflowBlockOptions">The options with which to configure this <see cref="FilterBlock{T}"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="predicate"/> or <paramref name="dataflowBlockOptions"/> is <see langword="null"/>.
        /// </exception>
        public FilterBlock(Func<T, bool> predicate, ExecutionDataflowBlockOptions dataflowBlockOptions)
        {
            this._inputBlock = new TransformBlock<T, (T, bool)>(CreateTransform(predicate), dataflowBlockOptions);
            this._outputBlock = new TransformWithoutBufferBlock<(T, bool), T>(TakeValue, dataflowBlockOptions.CancellationToken);
            this.Initialize();
        }

        /// <summary>
        /// Initializes a new <see cref="FilterBlock{T}"/>.
        /// </summary>
        /// <param name="predicate">
        /// The function that will be called for each item.
        /// If it returns <see langword="false"/>, the item will be dropped.
        /// </param>
        /// <param name="dataflowBlockOptions">The options with which to configure this <see cref="FilterBlock{T}"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="predicate"/> or <paramref name="dataflowBlockOptions"/> is <see langword="null"/>.
        /// </exception>
        public FilterBlock(Func<T, Task<bool>> predicate, ExecutionDataflowBlockOptions dataflowBlockOptions)
        {
            this._inputBlock = new TransformBlock<T, (T, bool)>(CreateTransform(predicate), dataflowBlockOptions);
            this._outputBlock = new TransformWithoutBufferBlock<(T, bool), T>(TakeValue, dataflowBlockOptions.CancellationToken);
            this.Initialize();
        }

        private static Func<T, (T, bool)> CreateTransform(Func<T, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return x => (x, predicate(x));
        }

        private static Func<T, Task<(T, bool)>> CreateTransform(Func<T, Task<bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return async x => (x, await predicate(x).ConfigureAwait(false));
        }

        private static T TakeValue((T, bool) tuple)
        {
            if (!tuple.Item2) throw new InvalidOperationException("The filtered item should be sent to the DropBlock.");
            return tuple.Item1;
        }

        private void Initialize()
        {
            this._inputBlock.LinkTo(new DropBlock<(T, bool)>(), t => !t.Item2);
            this._inputBlock.LinkWithCompletion(this._outputBlock);
        }

        /// <inheritdoc/>
        public Task Completion => this._inputBlock.Completion;

        /// <inheritdoc/>
        public void Complete() => this._inputBlock.Complete();

        void IDataflowBlock.Fault(Exception exception) => ((IDataflowBlock)this._inputBlock).Fault(exception);

        DataflowMessageStatus ITargetBlock<T>.OfferMessage(DataflowMessageHeader messageHeader, T messageValue, ISourceBlock<T>? source, bool consumeToAccept)
            => ((ITargetBlock<T>)this._inputBlock).OfferMessage(messageHeader, messageValue, source != null ? new ProxySourceBlock<T>(this, source) : null, consumeToAccept);

        /// <inheritdoc/>
        public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions)
            => this._outputBlock.LinkTo(target, linkOptions);

        T ISourceBlock<T>.ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
            => ((ISourceBlock<T>)this._outputBlock).ConsumeMessage(messageHeader, target, out messageConsumed);

        bool ISourceBlock<T>.ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
            => ((ISourceBlock<T>)this._outputBlock).ReserveMessage(messageHeader, target);

        void ISourceBlock<T>.ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
            => ((ISourceBlock<T>)this._outputBlock).ReleaseReservation(messageHeader, target);

        /// <inheritdoc/>
        public bool TryReceive(Predicate<T>? filter, out T item)
        {
            var result = this._inputBlock.TryReceive(
                t => t.Item2 && (filter == null || filter(t.Item1)),
                out var item2);
            item = item2.Item1;
            return result;
        }

        /// <inheritdoc/>
        public bool TryReceiveAll(out IList<T>? items)
        {
            var result = this._inputBlock.TryReceiveAll(out var items2);
            if (!result)
            {
                items = null;
                return false;
            }

            items = items2.Where(t => t.Item2).Select(t => t.Item1).ToList();
            return items.Count > 0;
        }
    }
}
