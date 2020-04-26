using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Fluent;
using BiDaFlow.Internal;

namespace BiDaFlow.Blocks
{
    public class TransformWithoutBufferBlock<TInput, TOutput> : IPropagatorBlock<TInput, TOutput>
    {
        private readonly Func<TInput, TOutput> _transform;
        private readonly TaskFactory _taskFactory;
        private readonly TaskCompletionSource<ValueTuple> _tcs;
        private readonly LinkedList<LinkedTarget<TOutput>> _targets = new LinkedList<LinkedTarget<TOutput>>();
        private bool _isCompleted;

        private long _nextId;
        private readonly LinkedList<OfferingMessage> _offeringMessages = new LinkedList<OfferingMessage>();

        public TransformWithoutBufferBlock(Func<TInput, TOutput> transform, TaskScheduler taskScheduler, CancellationToken cancellationToken)
        {
            this._transform = transform ?? throw new ArgumentNullException(nameof(transform));
            this._taskFactory = new TaskFactory(taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler)));
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
        }

        public TransformWithoutBufferBlock(Func<TInput, TOutput> transform)
            : this(transform, TaskScheduler.Default, CancellationToken.None) { }

        public TransformWithoutBufferBlock(Func<TInput, TOutput> transform, CancellationToken cancellationToken)
            : this(transform, TaskScheduler.Default, cancellationToken) { }

        private object Lock => this._tcs; // any readonly object

        public Task Completion => this._tcs.Task;

        public void Complete()
        {
            if (this._isCompleted) return;

            lock (this.Lock)
            {
                this._isCompleted = true;
            }

            this._tcs.TrySetResult(default);
        }

        void IDataflowBlock.Fault(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            if (this._isCompleted) return;

            lock (this.Lock)
            {
                this._isCompleted = true;
            }

            if (exception is AggregateException aex)
            {
                this._tcs.TrySetException(aex.InnerExceptions);
            }
            else
            {
                this._tcs.TrySetException(exception);
            }
        }

        public IDisposable LinkTo(ITargetBlock<TOutput> target, DataflowLinkOptions linkOptions)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            linkOptions ??= new DataflowLinkOptions();

            if (linkOptions.MaxMessages == 0) return ActionDisposable.Nop;

            var unlinkerForCompletion = linkOptions.PropagateCompletion
                ? this.PropagateCompletion(target)
                : null;

            var linkedTarget = new LinkedTarget<TOutput>(target, unlinkerForCompletion, linkOptions.MaxMessages);

            lock (this.Lock)
            {
                if (linkOptions.Append)
                {
                    this._targets.AddLast(linkedTarget);
                }
                else
                {
                    this._targets.AddFirst(linkedTarget);
                }
            }

            this.OfferToTargets();

            return new ActionDisposable(() =>
            {
                lock (this.Lock)
                {
                    linkedTarget.Unlink();
                }

                this.OfferToTargets();
            });
        }

        TOutput ISourceBlock<TOutput>.ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target, out bool messageConsumed)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            OfferingMessage? offeringMessage = null;

            lock (this.Lock)
            {
                for (var node = this._offeringMessages.First; node != null; node = node.Next)
                {
                    if (node.Value.MessageHeader == messageHeader)
                    {
                        if (node.Value.ReservedBy != null && node.Value.ReservedBy != target)
                        {
                            messageConsumed = false;
                            return default!;
                        }

                        offeringMessage = node.Value;
                        this._offeringMessages.Remove(node);
                        break;
                    }
                }
            }

            if (offeringMessage != null)
            {
                var consumedValue = offeringMessage.Source.ConsumeMessage(offeringMessage.SourceHeader, this, out messageConsumed);

                if (messageConsumed)
                {
                    try
                    {
                        return this._transform(consumedValue);
                    }
                    catch (Exception ex)
                    {
                        ((IDataflowBlock)this).Fault(ex);
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
                        if (offeringMessage.ReservedBy == target)
                        {
                            offeringMessage.ReservedBy = null;
                            break;
                        }

                        return;
                    }
                }
            }

            if (offeringMessage == null) return;

            offeringMessage!.Source.ReleaseReservation(offeringMessage.SourceHeader, this);

            this.OfferToTargets();
        }

        DataflowMessageStatus ITargetBlock<TInput>.OfferMessage(DataflowMessageHeader messageHeader, TInput messageValue, ISourceBlock<TInput>? source, bool consumeToAccept)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (consumeToAccept && source == null) throw new ArgumentException("source is null though consumeToAccept is true.");

            if (this._isCompleted) return DataflowMessageStatus.DecliningPermanently;

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

            lock (this.Lock)
            {
                if (this._isCompleted) return DataflowMessageStatus.DecliningPermanently;

                var canOfferNow = this._offeringMessages.Count == 0;
                DataflowMessageHeader myHeader = default;
                LinkedListNode<OfferingMessage>? enqueuedNode = null;

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

                Debug.Assert(myHeader.IsValid);

                if (canOfferNow)
                {
                    for (var targetNode = this._targets.First; targetNode != null;)
                    {
                        var linkedTarget = targetNode.Value;
                        var remove = false;

                        if (linkedTarget.IsLinked)
                        {
                            var status = linkedTarget.Target.OfferMessage(myHeader, transformedValue, source == null ? null : this, consumeToAccept);

                            switch (status)
                            {
                                case DataflowMessageStatus.Accepted:
                                    linkedTarget.DecrementRemainingMessages();
                                    goto case DataflowMessageStatus.NotAvailable;

                                case DataflowMessageStatus.NotAvailable:
                                    if (enqueuedNode?.List != null)
                                        this._offeringMessages.Remove(enqueuedNode);

                                    return status;

                                case DataflowMessageStatus.Postponed:
                                    return DataflowMessageStatus.Postponed;

                                case DataflowMessageStatus.DecliningPermanently:
                                    linkedTarget.Unlink();
                                    remove = true;
                                    break;
                            }
                        }
                        else
                        {
                            remove = true;
                        }

                        if (remove)
                        {
                            var nodeToRemove = targetNode;
                            targetNode = targetNode.Next;
                            this._targets.Remove(nodeToRemove);
                        }
                        else
                        {
                            targetNode = targetNode.Next;
                        }
                    }
                }
            }

            return source == null ? DataflowMessageStatus.Declined : DataflowMessageStatus.Postponed;
        }

        private void OfferToTargets()
        {
            if (this._isCompleted) return;

            this._taskFactory.StartNew(() =>
            {
                lock (this.Lock)
                {
                    try
                    {
                    StartConsume:
                        if (this._isCompleted) return;

                        var messageNode = this._offeringMessages.First;
                        if (messageNode == null || messageNode.Value.ReservedBy != null) return;

                        var message = messageNode.Value;

                        for (var targetNode = this._targets.First; targetNode != null;)
                        {
                            var linkedTarget = targetNode.Value;
                            var remove = false;

                            if (linkedTarget.IsLinked)
                            {
                                var status = linkedTarget.Target.OfferMessage(message.MessageHeader, message.TransformedValue, this, true);

                                switch (status)
                                {
                                    case DataflowMessageStatus.Accepted:
                                        linkedTarget.DecrementRemainingMessages();
                                        goto case DataflowMessageStatus.NotAvailable;

                                    case DataflowMessageStatus.NotAvailable:
                                        if (messageNode.List != null)
                                            throw new InvalidOperationException("The message has not been consumed.");

                                        goto StartConsume;

                                    case DataflowMessageStatus.Postponed:
                                        return;

                                    case DataflowMessageStatus.DecliningPermanently:
                                        linkedTarget.Unlink();
                                        remove = true;
                                        break;
                                }
                            }
                            else
                            {
                                remove = true;
                            }

                            if (remove)
                            {
                                var nodeToRemove = targetNode;
                                targetNode = targetNode.Next;
                                this._targets.Remove(nodeToRemove);
                            }
                            else
                            {
                                targetNode = targetNode.Next;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ((IDataflowBlock)this).Fault(ex);
                    }
                }
            });
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
