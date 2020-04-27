using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Internal
{
    internal class SourceCore<T>
    {
        private readonly ISourceBlock<T> _parent;
        private readonly TaskFactory _taskFactory;
        private readonly Action? _readyToNextItemCallback;
        private readonly LinkedList<LinkedTarget2<T>> _links = new LinkedList<LinkedTarget2<T>>();
        private readonly Dictionary<ITargetBlock<T>, LinkedListNode<LinkedTarget2<T>>> _targetToLinkTable = new Dictionary<ITargetBlock<T>, LinkedListNode<LinkedTarget2<T>>>();

        private long _messageId = 1;
        private bool _itemAvailable;
        private T _offeringItem = default!;
        private ITargetBlock<T>? _resevedBy;
        private bool _calledReadyCallback;

        private bool _completed;

        public SourceCore(ISourceBlock<T> parent, TaskScheduler taskScheduler, Action? readyToNextItemCallback)
        {
            this._parent = parent ?? throw new ArgumentNullException(nameof(parent));
            this._taskFactory = new TaskFactory(taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler)));
            this._readyToNextItemCallback = readyToNextItemCallback;

            parent.Completion.ContinueWith(this.HandleCompletion, taskScheduler);
        }

        private object Lock => this._links; // any readonly object

        public bool ConsumeToAccept { get; set; }

        public void OfferItem(T item, bool? consumeToAccept = null)
        {
            lock (this.Lock)
            {
                this._offeringItem = item;
                this._itemAvailable = true;
                this._resevedBy = null;
            }

            this.OfferToTargets(consumeToAccept ?? this.ConsumeToAccept);
        }

        public void DismissItem()
        {
            lock (this.Lock)
            {
                this._offeringItem = default!;
                this._itemAvailable = false;
                this._calledReadyCallback = true;
            }
        }

        public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions? linkOptions)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            linkOptions ??= new DataflowLinkOptions();

            if (target.Completion.IsCompleted || linkOptions.MaxMessages == 0)
                return ActionDisposable.Nop;

            LinkedTarget2<T> linkedTarget;
            var callCallback = false;

            lock (this.Lock)
            {
                if (this._completed)
                {
                    var exception = this._parent.Completion.Exception;
                    if (exception == null)
                        target.Complete();
                    else
                        target.Fault(exception);

                    return ActionDisposable.Nop;
                }

                linkedTarget = new LinkedTarget2<T>(target, linkOptions.MaxMessages, linkOptions.PropagateCompletion, this.HandleUnlink);

                if (linkOptions.Append)
                {
                    var node = this._links.AddLast(linkedTarget);
                    if (!this._targetToLinkTable.ContainsKey(target))
                        this._targetToLinkTable.Add(target, node);
                }
                else
                {
                    var node = this._links.AddFirst(linkedTarget);
                    this._targetToLinkTable[target] = node;
                }

                if (!this._itemAvailable && !this._calledReadyCallback)
                {
                    this._calledReadyCallback = true;
                    callCallback = true;
                }
            }

            if (callCallback) this._readyToNextItemCallback?.Invoke();

            this.OfferToTargets(this.ConsumeToAccept);

            return new ActionDisposable(linkedTarget.Unlink);
        }

        public T ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            T output;

            lock (this.Lock)
            {
                if (this._messageId != messageHeader.Id || (this._resevedBy != null && this._resevedBy != target))
                {
                    messageConsumed = false;
                    return default!;
                }

                if (this._resevedBy != null)
                {
                    this._resevedBy = null;
                }

                messageConsumed = this._itemAvailable;
                this._itemAvailable = false;
                this._resevedBy = null;
                this._messageId++;
                output = this._offeringItem;

                if (messageConsumed && this._targetToLinkTable.TryGetValue(target, out var linkedTargetNode))
                    linkedTargetNode.Value.DecrementRemainingMessages();

                // If the link has reached to MaxMessages, HandleUnlink has been called in DecrementRemainingMessages.
                // Here we can check whether a link is left.
                this._calledReadyCallback = this._links.Count > 0;
            }

            if (this._calledReadyCallback) this._readyToNextItemCallback?.Invoke();

            return output;
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            lock (this.Lock)
            {
                if (this._resevedBy == null && this._messageId == messageHeader.Id)
                {
                    this._resevedBy = target;
                    return true;
                }
            }

            return false;
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            lock (this.Lock)
            {
                if (this._resevedBy == target && this._messageId == messageHeader.Id)
                {
                    this._resevedBy = null;
                }
                else
                {
                    throw new InvalidOperationException("The message has not been reserved by the target.");
                }
            }

            this.OfferToTargets(this.ConsumeToAccept);
        }

        private void HandleUnlink(LinkedTarget2<T> linkedTarget)
        {
            if (linkedTarget == null) throw new ArgumentNullException(nameof(linkedTarget));

            lock (this.Lock)
            {
                if (this._completed) return;

                // Remove from the list of linked targets
                var node = this._targetToLinkTable[linkedTarget.Target];
                if (node.Value == linkedTarget)
                {
                    var nextSameTargetNode = node.Next;
                    for (; nextSameTargetNode != null; nextSameTargetNode = nextSameTargetNode.Next)
                    {
                        if (Equals(nextSameTargetNode.Value.Target, linkedTarget.Target))
                            break;
                    }

                    this._links.Remove(node);

                    if (nextSameTargetNode == null)
                    {
                        var removed = this._targetToLinkTable.Remove(linkedTarget.Target);
                        Debug.Assert(removed);

                        // Release reservation
                        if (Equals(this._resevedBy, linkedTarget.Target))
                        {
                            this._resevedBy = null;
                            this.OfferToTargets(this.ConsumeToAccept);
                        }
                    }
                    else
                    {
                        this._targetToLinkTable[linkedTarget.Target] = nextSameTargetNode;
                    }
                }
                else
                {
                    this._links.Remove(linkedTarget);
                }
            }
        }

        private void HandleCompletion(Task completionTask)
        {
            lock (this.Lock)
            {
                if (this._completed) return;
                this._completed = true;
            }

            // There is no writer after setting true to _completed.

            foreach (var x in this._links)
                x.Complete(completionTask.Exception);
        }

        private void OfferToTargets(bool consumeToAccept)
        {
            lock (this.Lock)
            {
                if (!this.CanOffer()) return;
            }

            this._taskFactory.StartNew(
                consumeToAccept
                    ? (Action<object>)(state => ((SourceCore<T>)state).OfferCoreConsumeToAccept())
                    : state => ((SourceCore<T>)state).OfferCore(),
                this
            );
        }

        private void OfferCore()
        {
        StartOffer:
            lock (this.Lock)
            {
                if (!CanOffer()) return;

                var header = new DataflowMessageHeader(this._messageId);

                for (var node = this._links.First; node != null;)
                {
                    var linkedTarget = node.Value;
                    var nextNode = node.Next;

                    Debug.Assert(!linkedTarget.Unlinked);

                    var status = linkedTarget.Target.OfferMessage(header, this._offeringItem, this._parent, false);

                    switch (status)
                    {
                        case DataflowMessageStatus.Accepted:
                            this._itemAvailable = false;
                            this._messageId++;
                            linkedTarget.DecrementRemainingMessages();

                            // If the link has reached to MaxMessages, HandleUnlink has been called in DecrementRemainingMessages.
                            // Here we can check whether a link is left.
                            this._calledReadyCallback = !this._itemAvailable && this._links.Count > 0;

                            if (this._calledReadyCallback)
                                goto CallReadyCallback;
                            else
                                goto StartOffer;

                        case DataflowMessageStatus.NotAvailable:
                            throw new InvalidOperationException("the target returns NotAvailable even though consumeToAccept is false.");

                        case DataflowMessageStatus.DecliningPermanently:
                            linkedTarget.Unlink();
                            break;
                    }

                    node = nextNode;
                }
            }

        CallReadyCallback:
            this._readyToNextItemCallback?.Invoke();
            goto StartOffer;
        }

        private void OfferCoreConsumeToAccept()
        {
        StartOffer:
            DataflowMessageHeader messageHeader;
            T messageValue;

            lock (this.Lock)
            {
                if (!this.CanOffer()) return;

                messageHeader = new DataflowMessageHeader(this._messageId);
                messageValue = this._offeringItem;
            }

            // We can offer without lock because ConsumeMessage will lock and checke the state.

            for (var node = this._links.First; node != null;)
            {
                var linkedTarget = node.Value;
                var nextNode = node.Next;

                var status = linkedTarget.Target.OfferMessage(messageHeader, messageValue, this._parent, true);

                switch (status)
                {
                    case DataflowMessageStatus.Accepted:
                    case DataflowMessageStatus.NotAvailable:
                        goto StartOffer;

                    case DataflowMessageStatus.DecliningPermanently:
                        linkedTarget.Unlink();
                        break;
                }

                node = nextNode;
            }
        }

        private bool CanOffer() => this._itemAvailable && this._resevedBy == null;
    }
}
