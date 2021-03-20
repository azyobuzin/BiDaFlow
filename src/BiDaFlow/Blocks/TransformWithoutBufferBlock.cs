using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Internal;

namespace BiDaFlow.Blocks
{
    /// <summary>
    /// Provides a dataflow block like <seealso cref="TransformBlock{TInput, TOutput}"/>.
    /// This block does not consume items from source blocks until offering a message to link targets succeeds.
    /// </summary>
    public class TransformWithoutBufferBlock<TInput, TOutput> : IPropagatorBlock<TInput, TOutput>
    {
        private readonly Func<TInput, TOutput> _transform;
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenRegistration _cancelReg;
        private readonly TaskCompletionSource<ValueTuple> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly LinkManager<TOutput> _linkManager = new();
        private readonly List<Exception> _exceptions = new();
        private readonly TaskSchedulerAutoResetEvent _offerEvent;

        private bool _completeRequested;
        private long _messageId = 1;
        private ITargetBlock<TOutput>? _reservedBy;
        private readonly Queue<OfferedMessage> _queue = new Queue<OfferedMessage>();

        /// <summary>
        /// Initializes a new <see cref="TransformWithoutBufferBlock{TInput, TOutput}"/>.
        /// </summary>
        /// 
        /// <param name="transform">
        /// A transform function.
        /// <para>Note that this function should be pure because it can be called multiple times for the same item.</para>
        /// </param>
        /// 
        /// <param name="taskScheduler">A <see cref="TaskScheduler"/> used by offering messages.</param>
        /// 
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> to monitor for cancellation requests.
        /// <para>When cancellation is requested, this block behaves like when <see cref="Complete"/> is called.</para>
        /// </param>
        /// 
        /// <exception cref="ArgumentNullException">
        /// <paramref name="transform"/> or <paramref name="taskScheduler"/> is <see langword="null"/>.
        /// </exception>
        public TransformWithoutBufferBlock(Func<TInput, TOutput> transform, TaskScheduler taskScheduler, CancellationToken cancellationToken)
        {
            this._transform = transform;
            this._cancellationToken = cancellationToken;
            this._offerEvent = new TaskSchedulerAutoResetEvent(false, taskScheduler);

            if (cancellationToken.CanBeCanceled)
            {
                this._cancelReg = cancellationToken.Register(state => ((IDataflowBlock)state).Complete(), this);
            }

            _ = OfferWorkerAsync();
        }

        /// <summary>
        /// Initializes a new <see cref="TransformWithoutBufferBlock{TInput, TOutput}"/>.
        /// </summary>
        /// 
        /// <param name="transform">
        /// A transform function.
        /// <para>Note that this function should be pure because it can be called multiple times for the same item.</para>
        /// </param>
        /// 
        /// <exception cref="ArgumentNullException"><paramref name="transform"/> is <see langword="null"/>.</exception>
        /// 
        /// <remarks>
        /// This overload calls <see cref="TransformWithoutBufferBlock{TInput, TOutput}.TransformWithoutBufferBlock(Func{TInput, TOutput}, TaskScheduler, CancellationToken)"/>
        /// with <see cref="TaskScheduler.Default"/> and <see cref="CancellationToken.None"/>.
        /// </remarks>
        public TransformWithoutBufferBlock(Func<TInput, TOutput> transform)
            : this(transform, TaskScheduler.Default, CancellationToken.None) { }

        /// <summary>
        /// Initializes a new <see cref="TransformWithoutBufferBlock{TInput, TOutput}"/>.
        /// </summary>
        /// 
        /// <param name="transform">
        /// A transform function.
        /// <para>Note that this function should be pure because it can be called multiple times for the same item.</para>
        /// </param>
        /// 
        /// <param name="cancellationToken">
        /// A <see cref="CancellationToken"/> to monitor for cancellation requests.
        /// <para>When cancellation is requested, this block behaves like when <see cref="Complete"/> is called.</para>
        /// </param>
        /// 
        /// <exception cref="ArgumentNullException"><paramref name="transform"/> is <see langword="null"/>.</exception>
        /// 
        /// <remarks>
        /// This overload calls <see cref="TransformWithoutBufferBlock{TInput, TOutput}.TransformWithoutBufferBlock(Func{TInput, TOutput}, TaskScheduler, CancellationToken)"/>
        /// with <see cref="TaskScheduler.Default"/>.
        /// </remarks>
        public TransformWithoutBufferBlock(Func<TInput, TOutput> transform, CancellationToken cancellationToken)
            : this(transform, TaskScheduler.Default, cancellationToken) { }

        /// <summary>
        /// A lock object to prevent the item being consumed concurrently
        /// </summary>
        private object ItemLock => this._linkManager;

        private object CompletionLock => this._tcs;

        /// <inheritdoc cref="IDataflowBlock.Completion"/>
        public Task Completion => this._tcs.Task;

        /// <summary>
        /// Signals to the block to stop consuming and offering messages.
        /// </summary>
        /// <remarks>
        /// When this method is called, <see cref="Completion"/> will immediately be completed
        /// because this block has no buffer.
        /// </remarks>
        public void Complete()
        {
            this._completeRequested = true;
            this._offerEvent.Set();
        }

        void IDataflowBlock.Fault(Exception exception)
        {
            this.AddException(exception);
            this.Complete();
        }

        /// <inheritdoc cref="ISourceBlock{TOutput}.LinkTo(ITargetBlock{TOutput}, DataflowLinkOptions)"/>
        public IDisposable LinkTo(ITargetBlock<TOutput> target, DataflowLinkOptions linkOptions)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            linkOptions ??= new DataflowLinkOptions();

            LinkRegistration<TOutput> registration;

            lock (this.CompletionLock)
            {
                if (this.Completion.IsCompleted)
                {
                    var exception = this.Completion.Exception;
                    if (exception == null)
                        target.Complete();
                    else
                        target.Fault(exception);

                    return ActionDisposable.Nop;
                }

                registration = new LinkRegistration<TOutput>(target, linkOptions.MaxMessages, linkOptions.PropagateCompletion, this.HandleUnlink);
                this._linkManager.AddLink(registration, linkOptions.Append);
            }

            this.OfferToTargets();

            return new ActionDisposable(registration.Unlink);
        }

        TOutput ISourceBlock<TOutput>.ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target, out bool messageConsumed)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            TInput consumedValue;
            TOutput output = default!;

            lock (this.ItemLock)
            {
                if (this._messageId != messageHeader.Id ||
                    (this._reservedBy != null && !Equals(this._reservedBy, target)) ||
                    this._queue.Count == 0)
                {
                    messageConsumed = false;
                    return default!;
                }

                var offeredMessage = this._queue.Dequeue();
                consumedValue = offeredMessage.Source.ConsumeMessage(offeredMessage.SourceHeader, this, out messageConsumed);

                this._reservedBy = null;
                this._messageId++;

                if (messageConsumed)
                    this._linkManager.GetRegistration(target)?.DecrementRemainingMessages();
            }

            if (messageConsumed)
            {
                try
                {
                    output = this._transform(consumedValue);
                }
                catch (Exception ex)
                {
                    ((IDataflowBlock)this).Fault(ex);
                    messageConsumed = false;
                    return default!;
                }
            }

            this.OfferToTargets();

            return output;
        }

        bool ISourceBlock<TOutput>.ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            lock (this.ItemLock)
            {
                if (this._reservedBy == null && this._messageId == messageHeader.Id && this._queue.Count > 0)
                {
                    var offeredMessage = this._queue.Peek();
                    if (!offeredMessage.Source.ReserveMessage(offeredMessage.SourceHeader, this))
                        return false;

                    this._reservedBy = target;
                    return true;
                }
            }

            return false;
        }

        void ISourceBlock<TOutput>.ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            lock (this.ItemLock)
            {
                if (Equals(this._reservedBy, target) && this._messageId == messageHeader.Id)
                {
                    this._reservedBy = null;

                    if (this._queue.Count > 0)
                    {
                        var offeredMessage = this._queue.Peek();
                        offeredMessage.Source.ReleaseReservation(offeredMessage.SourceHeader, this);
                    }
                }
                else
                {
                    throw new InvalidOperationException("The message has not been reserved by the target.");
                }
            }

            this.OfferToTargets();
        }

        DataflowMessageStatus ITargetBlock<TInput>.OfferMessage(DataflowMessageHeader messageHeader, TInput messageValue, ISourceBlock<TInput>? source, bool consumeToAccept)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (consumeToAccept && source == null) throw new ArgumentException("source is null though consumeToAccept is true.");

            if (this._completeRequested) return DataflowMessageStatus.DecliningPermanently;

            TOutput transformedValue;

            try
            {
                transformedValue = this._transform(messageValue);
            }
            catch (Exception ex)
            {
                ((IDataflowBlock)this).Fault(ex);
                return DataflowMessageStatus.DecliningPermanently;
            }

            lock (this.ItemLock)
            {
                var canOfferNow = this._queue.Count == 0;
                this._queue.Enqueue(new OfferedMessage(source!, messageHeader, transformedValue));

                if (canOfferNow)
                {
                    var myHeader = new DataflowMessageHeader(this._messageId);

                    foreach (var registration in this._linkManager)
                    {
                        // The item can be reserved in OfferMessage
                        if (this._reservedBy != null) break;

                        if (registration.Unlinked) continue;

                        var status = registration.Target.OfferMessage(myHeader, transformedValue, this, consumeToAccept);

                        switch (status)
                        {
                            case DataflowMessageStatus.Accepted:
                                if (!consumeToAccept)
                                    registration.DecrementRemainingMessages();
                                goto case DataflowMessageStatus.NotAvailable;

                            case DataflowMessageStatus.NotAvailable:
                                if (!consumeToAccept)
                                {
                                    this._queue.Dequeue();
                                    this._messageId++;
                                }
                                return status;

                            case DataflowMessageStatus.DecliningPermanently:
                                registration.Unlink();
                                break;
                        }
                    }
                }
            }

            return DataflowMessageStatus.Postponed;
        }

        private async Task OfferWorkerAsync()
        {
            try
            {
                while (!this._completeRequested)
                {
                    await this._offerEvent;

                    if (this._reservedBy != null) continue;

                    lock (this.ItemLock)
                    {
                    StartOffer:
                        if (this._queue.Count > 0)
                        {
                            var messageHeader = new DataflowMessageHeader(this._messageId);
                            var offeredMessage = this._queue.Peek();

                            foreach (var registration in this._linkManager)
                            {
                                // The item can be reserved in OfferMessage
                                if (this._reservedBy != null) break;

                                if (registration.Unlinked) continue;

                                var status = registration.Target.OfferMessage(messageHeader, offeredMessage.TransformedValue, this, true);

                                switch (status)
                                {
                                    case DataflowMessageStatus.Accepted:
                                    case DataflowMessageStatus.NotAvailable:
                                        goto StartOffer;

                                    case DataflowMessageStatus.DecliningPermanently:
                                        registration.Unlink();
                                        break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.AddException(ex);
            }

            this._cancelReg.Dispose();
            this.CompleteCore();
        }

        private void OfferToTargets()
        {
            if (this._reservedBy != null || this._queue.Count == 0 || this._linkManager.Count == 0) return;
            this._offerEvent.Set();
        }

        private void AddException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));

            lock (this._exceptions)
                this._exceptions.Add(exception);
        }

        private void CompleteCore()
        {
            this.ReleasePostponedMessages();

            lock (this.CompletionLock)
            {
                var isError = false;
                lock (this._exceptions)
                {
                    if (this._exceptions.Count > 0)
                    {
                        isError = true;
                        this._tcs.SetException(this._exceptions);
                    }
                }

                if (!isError)
                {
                    if (this._cancellationToken.IsCancellationRequested)
                    {
                        this._tcs.TrySetCanceled(this._cancellationToken);
                    }
                    else
                    {
                        this._tcs.SetResult(default);
                    }
                }

                var completionTask = this.Completion;
                Debug.Assert(completionTask.IsCompleted);

                foreach (var registration in this._linkManager)
                    registration.Complete(completionTask.Exception);
            }
        }

        private void HandleUnlink(LinkRegistration<TOutput> registration)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));

            // Remove from the list of linked targets
            this._linkManager.RemoveLink(registration);

            if (this._linkManager.GetRegistration(registration.Target) == null)
            {
                // Release reservation
                var released = false;
                lock (this.ItemLock)
                {
                    if (Equals(this._reservedBy, registration.Target))
                    {
                        this._reservedBy = null;
                        released = true;
                    }
                }

                if (released)
                {
                    this._offerEvent.Set();
                }
            }
        }

        private void ReleasePostponedMessages()
        {
            lock (this.ItemLock)
            {
                while (this._queue.Count > 0)
                {
                    var offeredMessage = this._queue.Dequeue();

                    try
                    {
                        if (this._reservedBy != null || offeredMessage.Source.ReserveMessage(offeredMessage.SourceHeader, this))
                            offeredMessage.Source.ReleaseReservation(offeredMessage.SourceHeader, this);
                    }
                    catch (Exception ex)
                    {
                        this.AddException(ex);
                    }

                    this._reservedBy = null;
                }
            }
        }

        [StructLayout(LayoutKind.Auto)]
        private readonly struct OfferedMessage
        {
            public ISourceBlock<TInput> Source { get; }
            public DataflowMessageHeader SourceHeader { get; }
            public TOutput TransformedValue { get; }

            public OfferedMessage(ISourceBlock<TInput> source, DataflowMessageHeader sourceHeader, TOutput transformedValue)
            {
                this.Source = source;
                this.SourceHeader = sourceHeader;
                this.TransformedValue = transformedValue;
            }
        }
    }
}
