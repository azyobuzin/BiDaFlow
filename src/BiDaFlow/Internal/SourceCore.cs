using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Internal
{
    internal sealed class SourceCore<T>
    {
        private readonly ISourceBlock<T> _parent;
        private readonly TaskFactory _taskFactory;
        private readonly Action? _readyToNextItemCallback;
        private readonly LinkManager<T> _linkManager = new LinkManager<T>();

        private long _messageId = 1;
        private bool _itemAvailable;
        private T _offeringItem = default!;
        private ITargetBlock<T>? _resevedBy;
        private bool _calledReadyCallback;

        private bool _isCompleted;

        public SourceCore(ISourceBlock<T> parent, TaskScheduler taskScheduler, Action? readyToNextItemCallback)
        {
            this._parent = parent ?? throw new ArgumentNullException(nameof(parent));
            this._taskFactory = new TaskFactory(taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler)));
            this._readyToNextItemCallback = readyToNextItemCallback;

            parent.Completion.ContinueWith(this.HandleCompletion, taskScheduler);
        }

        /// <summary>
        /// Global lock object
        /// </summary>
        private object Lock => this._linkManager; // any readonly object

        /// <summary>
        /// A lock object to avoid running offer concurrently
        /// </summary>
        private object OfferLock { get; } = new object();

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

            LinkRegistration<T> registration;
            var callCallback = false;

            lock (this.Lock)
            {
                if (this._isCompleted)
                {
                    var exception = this._parent.Completion.Exception;
                    if (exception == null)
                        target.Complete();
                    else
                        target.Fault(exception);

                    return ActionDisposable.Nop;
                }

                registration = new LinkRegistration<T>(target, linkOptions.MaxMessages, linkOptions.PropagateCompletion, this.HandleUnlink);
                this._linkManager.AddLink(registration, linkOptions.Append);

                if (!this._itemAvailable && !this._calledReadyCallback)
                {
                    this._calledReadyCallback = true;
                    callCallback = true;
                }
            }

            if (callCallback) this._readyToNextItemCallback?.Invoke();

            this.OfferToTargets(this.ConsumeToAccept);

            return new ActionDisposable(registration.Unlink);
        }

        public T ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            T output;

            lock (this.Lock)
            {
                if (this._isCompleted)
                {
                    messageConsumed = false;
                    return default!;
                }

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

                if (messageConsumed)
                {
                    this._linkManager.GetRegistration(target)?.DecrementRemainingMessages();
                }

                // If the link has reached to MaxMessages, HandleUnlink has been called in DecrementRemainingMessages.
                // Here we can check whether a link is left.
                this._calledReadyCallback = this._linkManager.Count > 0;
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
                if (this._isCompleted) return false;

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

        private void HandleUnlink(LinkRegistration<T> registration)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));

            lock (this.Lock)
            {
                if (this._isCompleted) return;

                // Remove from the list of linked targets
                this._linkManager.RemoveLink(registration);
            }

            if (this._linkManager.GetRegistration(registration.Target) == null)
            {
                // Release reservation
                if (Equals(this._resevedBy, registration.Target))
                {
                    this._resevedBy = null;
                    this.OfferToTargets(this.ConsumeToAccept);
                }
            }
        }

        private void HandleCompletion(Task completionTask)
        {
            lock (this.Lock)
            {
                if (this._isCompleted) return;
                this._isCompleted = true;
            }

            foreach (var registration in this._linkManager)
                registration.Complete(completionTask.Exception);
        }

        private void OfferToTargets(bool consumeToAccept)
        {
            if (!this.CanOffer()) return;

            this._taskFactory.StartNew(() =>
            {
                try
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

                    lock (this.OfferLock)
                    {
                        foreach (var registration in this._linkManager)
                        {
                            var status = registration.Target.OfferMessage(messageHeader, messageValue, this._parent, true);

                            switch (status)
                            {
                                case DataflowMessageStatus.Accepted:
                                    lock (this.Lock)
                                    {
                                        if (!consumeToAccept)
                                        {
                                            this._itemAvailable = false;
                                            this._messageId++;
                                            registration.DecrementRemainingMessages();
                                        }

                                        // If the link has reached to MaxMessages, HandleUnlink has been called in DecrementRemainingMessages.
                                        // Here we can check whether a link is left.
                                        this._calledReadyCallback = !this._itemAvailable && this._linkManager.Count > 0;

                                        if (this._calledReadyCallback)
                                            goto CallReadyCallback;
                                        else
                                            goto StartOffer;
                                    }

                                case DataflowMessageStatus.NotAvailable:
                                    goto StartOffer;

                                case DataflowMessageStatus.DecliningPermanently:
                                    registration.Unlink();
                                    break;
                            }
                        }
                    }

                    goto StartOffer;

                CallReadyCallback:
                    this._readyToNextItemCallback?.Invoke();
                    goto StartOffer;
                }
                catch (Exception ex)
                {
                    this._parent.Fault(ex);
                }
            });
        }

        private bool CanOffer() => this._itemAvailable && this._resevedBy == null;
    }
}
