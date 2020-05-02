using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly TaskFactory _taskFactory;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<ValueTuple> _tcs;
        private readonly LinkManager<TOutput> _linkManager = new LinkManager<TOutput>();
        private bool _completeRequested;
        private bool _propagatedCompletion;

        private long _nextId;
        private readonly LinkedList<OfferingMessage> _offeringMessages = new LinkedList<OfferingMessage>(); // TODO: more efficient structure

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
            this._transform = transform ?? throw new ArgumentNullException(nameof(transform));
            this._taskFactory = new TaskFactory(taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler)));
            this._cancellationToken = cancellationToken;
            this._tcs = new TaskCompletionSource<ValueTuple>();

            if (cancellationToken.CanBeCanceled)
            {
                var reg = cancellationToken.Register(state => ((IDataflowBlock)state).Complete(), this);
                this.Completion.ContinueWith(
                    (_, state) => ((IDisposable)state).Dispose(),
                    reg,
                    cancellationToken,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            this.Completion.ContinueWith(this.HandleCompletion, taskScheduler);
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
        /// Global lock object
        /// </summary>
        private object Lock => this._tcs; // any readonly object

        /// <summary>
        /// A lock object to avoid running offer concurrently
        /// </summary>
        private object OfferLock => this._offeringMessages;

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
            this.CompleteCore(null);
        }

        void IDataflowBlock.Fault(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));

            this.CompleteCore(exception);
        }

        /// <inheritdoc cref="ISourceBlock{TOutput}.LinkTo(ITargetBlock{TOutput}, DataflowLinkOptions)"/>
        public IDisposable LinkTo(ITargetBlock<TOutput> target, DataflowLinkOptions linkOptions)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            linkOptions ??= new DataflowLinkOptions();

            LinkRegistration<TOutput> registration;

            lock (this.Lock)
            {
                if (this._propagatedCompletion)
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

            OfferingMessage? offeringMessage = null;

            lock (this.Lock)
            {
                if (this._completeRequested)
                {
                    messageConsumed = false;
                    return default!;
                }

                for (var node = this._offeringMessages.First; node != null; node = node.Next)
                {
                    if (node.Value.MessageHeader == messageHeader)
                    {
                        if (node.Value.ReservedBy != null && !Equals(node.Value.ReservedBy, target))
                        {
                            messageConsumed = false;
                            return default!;
                        }

                        offeringMessage = node.Value;
                        this._offeringMessages.Remove(node);
                        break;
                    }
                }

                if (offeringMessage != null)
                {
                    var consumedValue = offeringMessage.Source.ConsumeMessage(offeringMessage.SourceHeader, this, out messageConsumed);

                    if (messageConsumed)
                    {
                        try
                        {
                            var transformedValue = this._transform(consumedValue);

                            this._linkManager.GetRegistration(target)?.DecrementRemainingMessages();

                            return transformedValue;
                        }
                        catch (Exception ex)
                        {
                            ((IDataflowBlock)this).Fault(ex);
                        }
                    }
                }
            }

            messageConsumed = false;
            return default!;
        }

        bool ISourceBlock<TOutput>.ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            lock (this.Lock)
            {
                if (this._completeRequested) return false;

                foreach (var offeringMessage in this._offeringMessages)
                {
                    if (offeringMessage.MessageHeader == messageHeader)
                    {
                        if (offeringMessage.ReservedBy == null &&
                            offeringMessage.Source.ReserveMessage(offeringMessage.SourceHeader, this))
                        {
                            offeringMessage.ReservedBy = target;
                            return true;
                        }

                        break;
                    }
                }
            }

            return false;
        }

        void ISourceBlock<TOutput>.ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            OfferingMessage? offeringMessage = null;

            lock (this.Lock)
            {
                for (var node = this._offeringMessages.First; node != null; node = node.Next)
                {
                    offeringMessage = node.Value;
                    if (offeringMessage.MessageHeader == messageHeader)
                    {
                        if (Equals(offeringMessage.ReservedBy, target))
                        {
                            offeringMessage.ReservedBy = null;
                            break;
                        }

                        return;
                    }
                }
            }

            if (offeringMessage == null) return;

            if (!this._completeRequested)
            {
                offeringMessage!.Source.ReleaseReservation(offeringMessage.SourceHeader, this);

                this.OfferToTargets();
            }
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

            bool canOfferNow;
            DataflowMessageHeader myHeader = default;
            LinkedListNode<OfferingMessage>? enqueuedNode = null;

            lock (this.Lock)
            {
                if (this._completeRequested) return DataflowMessageStatus.DecliningPermanently;

                canOfferNow = this._offeringMessages.Count == 0;

                if (source != null)
                {
                    // If source is not null, ConsumeMessage can be called.

                    for (var node = this._offeringMessages.First; node != null; node = node.Next)
                    {
                        var nodeMessage = node.Value;
                        if (nodeMessage.Source == source && nodeMessage.SourceHeader == messageHeader)
                        {
                            node.Value.TransformedValue = transformedValue;

                            myHeader = node.Value.MessageHeader;
                            enqueuedNode = node;

                            break;
                        }
                    }

                    if (enqueuedNode == null)
                    {
                        myHeader = new DataflowMessageHeader(++this._nextId);
                        enqueuedNode = this._offeringMessages.AddLast(new OfferingMessage(myHeader, source, messageHeader, transformedValue));
                    }
                }
                else
                {
                    myHeader = new DataflowMessageHeader(++this._nextId);
                }
            }

            Debug.Assert(myHeader.IsValid);

            if (canOfferNow)
            {
                lock (this.OfferLock)
                {
                    // Re-check whether the message is available
                    if (source == null || enqueuedNode!.List != null)
                    {
                        foreach (var registration in this._linkManager)
                        {
                            var status = registration.Target.OfferMessage(myHeader, transformedValue, this, consumeToAccept);

                            switch (status)
                            {
                                case DataflowMessageStatus.Accepted:
                                    if (!consumeToAccept)
                                    {
                                        lock (this.Lock)
                                            registration.DecrementRemainingMessages();
                                    }

                                    goto case DataflowMessageStatus.NotAvailable;

                                case DataflowMessageStatus.NotAvailable:
                                    if (!consumeToAccept && enqueuedNode != null)
                                    {
                                        lock (this.Lock)
                                            this._offeringMessages.Remove(enqueuedNode);
                                    }

                                    return status;

                                case DataflowMessageStatus.DecliningPermanently:
                                    registration.Unlink();
                                    break;
                            }
                        }
                    }
                }
            }

            return source == null ? DataflowMessageStatus.Declined : DataflowMessageStatus.Postponed;
        }

        private void OfferToTargets()
        {
            if (this._completeRequested) return;

            this._taskFactory.StartNew(() =>
            {
                try
                {
                StartConsume:
                    OfferingMessage message;

                    lock (this.Lock)
                    {
                        if (this._completeRequested) return;

                        var messageNode = this._offeringMessages.First;
                        if (messageNode == null || messageNode.Value.ReservedBy != null) return;

                        message = messageNode.Value;
                    }

                    lock (this.OfferLock)
                    {
                        foreach (var registration in this._linkManager)
                        {
                            var status = registration.Target.OfferMessage(message.MessageHeader, message.TransformedValue, this, true);

                            switch (status)
                            {
                                case DataflowMessageStatus.Accepted:
                                case DataflowMessageStatus.NotAvailable:
                                    goto StartConsume;

                                case DataflowMessageStatus.DecliningPermanently:
                                    registration.Unlink();
                                    break;
                            }
                        }
                    }

                    goto StartConsume;
                }
                catch (Exception ex)
                {
                    ((IDataflowBlock)this).Fault(ex);
                }
            });
        }

        private void HandleUnlink(LinkRegistration<TOutput> registration)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));

            if (this._completeRequested) return;

            // Remove from the list of linked targets
            this._linkManager.RemoveLink(registration);

            if (this._linkManager.GetRegistration(registration.Target) == null)
            {
                // Release reservation
                var releasedMessages = new List<OfferingMessage>();

                lock (this.Lock)
                {
                    foreach (var message in this._offeringMessages)
                    {
                        if (Equals(message.ReservedBy, registration.Target))
                        {
                            message.ReservedBy = null;
                            releasedMessages.Add(message);
                        }
                    }
                }

                if (releasedMessages.Count > 0)
                {
                    foreach (var message in releasedMessages)
                        message.Source.ReleaseReservation(message.SourceHeader, this);

                    this.OfferToTargets();
                }
            }
        }

        private void CompleteCore(Exception? exception)
        {
            lock (this.Lock)
            {
                if (this._completeRequested) return;
                this._completeRequested = true;
            }

            var exceptions = new List<Exception>();

            try
            {
                if (exception is AggregateException aex)
                    exceptions.AddRange(aex.InnerExceptions);
                else if (exception != null)
                    exceptions.Add(exception);

                this.ReleasePostponedMessages();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }

            if (exceptions.Count > 0)
            {
                this._tcs.TrySetException(exceptions);
            }
            else if (this._cancellationToken.IsCancellationRequested)
            {
                this._tcs.TrySetCanceled(this._cancellationToken);
            }
            else
            {
                this._tcs.TrySetResult(default);
            }
        }

        private void ReleasePostponedMessages()
        {
            foreach (var message in this._offeringMessages)
            {
                // https://github.com/dotnet/runtime/blob/89b8591928bcb9f90956c938fcd9fcfb2fdfb476/src/libraries/System.Threading.Tasks.Dataflow/src/Internal/Common.cs#L508-L511
                if (message.ReservedBy != null || message.Source.ReserveMessage(message.SourceHeader, this))
                {
                    message.Source.ReleaseReservation(message.SourceHeader, this);
                }
            }
        }

        private void HandleCompletion(Task completionTask)
        {
            lock (this.Lock)
            {
                if (this._propagatedCompletion) return;
                this._propagatedCompletion = true;
            }

            foreach (var registration in this._linkManager)
                registration.Complete(completionTask.Exception);
        }

        private class OfferingMessage
        {
            public DataflowMessageHeader MessageHeader { get; }
            public ISourceBlock<TInput> Source { get; }
            public DataflowMessageHeader SourceHeader { get; }
            public TOutput TransformedValue { get; set; }
            public ITargetBlock<TOutput>? ReservedBy { get; set; }

            public OfferingMessage(DataflowMessageHeader messageHeader, ISourceBlock<TInput> source, DataflowMessageHeader sourceHeader, TOutput transformedValue)
            {
                this.MessageHeader = messageHeader;
                this.Source = source;
                this.SourceHeader = sourceHeader;
                this.TransformedValue = transformedValue;
            }
        }
    }
}
