using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Internal
{
    internal sealed class SourceCore<T>
    {
        private readonly ISourceBlock<T> _parent;
        private readonly CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancelReg;
        private readonly TaskFactory _taskFactory;
        private readonly Action? _readyToNextItemCallback;
        private readonly LinkManager<T> _linkManager = new LinkManager<T>();
        private readonly Action _offerToTargetsDelegate;

        private long _messageId = 1;
        private bool _itemAvailable;
        private T _offeringItem = default!;
        private ITargetBlock<T>? _reservedBy;
        private bool _calledReadyCallback;

        private volatile bool _isCompleted; // TODO: Complete after _offeringItem is consumed

        public SourceCore(ISourceBlock<T> parent, TaskScheduler taskScheduler, CancellationToken cancellationToken, Action? readyToNextItemCallback)
        {
            this._parent = parent ?? throw new ArgumentNullException(nameof(parent));
            this._cancellationToken = cancellationToken;
            this._taskFactory = new TaskFactory(taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler)));
            this._readyToNextItemCallback = readyToNextItemCallback;
            this._offerToTargetsDelegate = this.OfferToTargets;

            this._cancelReg = cancellationToken.Register(this.Complete);
        }

        private object CompletionLock => this._linkManager; // any readonly object

        /// <summary>
        /// A lock object to prevent the item being consumed concurrently
        /// </summary>
        private object ItemLock { get; } = new object();

        public void OfferItem(T item)
        {
            lock (this.ItemLock)
            {
                this._offeringItem = item;
                this._itemAvailable = true;
                this._reservedBy = null;
            }

            this.OfferToTargets();
        }

        public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions? linkOptions)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            linkOptions ??= new DataflowLinkOptions();

            LinkRegistration<T> registration;
            var callCallback = false;

            lock (this.CompletionLock)
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
            }

            if (!this._itemAvailable && !this._calledReadyCallback)
            {
                this._calledReadyCallback = true;
                callCallback = true;
            }

            if (callCallback) this.CallReadyToNextItemCallback(true);

            this.OfferToTargetsOnTaskScheduler();

            return new ActionDisposable(registration.Unlink);
        }

        public T ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            T output;

            lock (this.ItemLock)
            {
                if (this._messageId != messageHeader.Id || (this._reservedBy != null && this._reservedBy != target))
                {
                    messageConsumed = false;
                    return default!;
                }

                if (this._reservedBy != null)
                {
                    this._reservedBy = null;
                }

                messageConsumed = this._itemAvailable;
                this._itemAvailable = false;
                this._reservedBy = null;
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

            if (this._calledReadyCallback && this._readyToNextItemCallback != null)
            {
                if (!this._isCompleted)
                    this.CallReadyToNextItemCallback(true);
            }

            return output;
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (this._isCompleted) return false;

            lock (this.ItemLock)
            {
                if (this._reservedBy == null && this._messageId == messageHeader.Id)
                {
                    this._reservedBy = target;
                    return true;
                }
            }

            return false;
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            lock (this.ItemLock)
            {
                if (this._reservedBy == target && this._messageId == messageHeader.Id)
                {
                    this._reservedBy = null;
                }
                else
                {
                    throw new InvalidOperationException("The message has not been reserved by the target.");
                }
            }

            this.OfferToTargetsOnTaskScheduler();
        }

        public void Complete()
        {
            // TODO
        }

        public void Fault(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            // TODO
        }

        private void HandleUnlink(LinkRegistration<T> registration)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));

            // Remove from the list of linked targets
            this._linkManager.RemoveLink(registration);

            if (this._linkManager.GetRegistration(registration.Target) == null)
            {
                // Release reservation
                lock (this.ItemLock)
                {
                    if (Equals(this._reservedBy, registration.Target))
                    {
                        this._reservedBy = null;
                        this.OfferToTargetsOnTaskScheduler();
                    }
                }
            }
        }

        private void HandleCompletion(Task completionTask)
        {
            lock (this.CompletionLock) // TODO
            {
                if (this._isCompleted) return;
                this._isCompleted = true;
            }

            foreach (var registration in this._linkManager)
                registration.Complete(completionTask.Exception);
        }

        private void OfferToTargets()
        {
            try
            {
            StartOffer:
                lock (this.ItemLock)
                {
                    if (!this.CanOffer()) return;

                    var messageHeader = new DataflowMessageHeader(this._messageId);
                    var messageValue = this._offeringItem;

                    foreach (var registration in this._linkManager)
                    {
                        var status = registration.Target.OfferMessage(messageHeader, messageValue, this._parent, false);

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

                goto StartOffer;
            }
            catch (Exception ex)
            {
                this._parent.Fault(ex);
            }
        }

        private void OfferToTargetsOnTaskScheduler()
        {
            if (!this.CanOffer()) return;
            this._taskFactory.StartNew(this._offerToTargetsDelegate);
        }

        private bool CanOffer() => this._itemAvailable && this._reservedBy == null;

        private void CallReadyToNextItemCallback(bool onTaskScheduler)
        {
            if (this._readyToNextItemCallback == null) return;

            if (onTaskScheduler)
            {
                this._taskFactory.StartNew(this._readyToNextItemCallback);
            }
            else
            {
                this._readyToNextItemCallback();
            }
        }
    }
}
