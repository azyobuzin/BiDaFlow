using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Internal
{
    internal sealed class EnumerableSourceCore<T>
    {
        private readonly ISourceBlock<T> _parent;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancelReg;
        private readonly LinkManager<T> _linkManager = new LinkManager<T>();
        private readonly TaskCompletionSource<ValueTuple> _tcs = new TaskCompletionSource<ValueTuple>();
        private readonly List<Exception> _exceptions = new List<Exception>();

        private readonly Action _enumerate;
        private readonly Action _offerToTargetsOnTaskScheduler;

        private int _state = (int)StateEnum.WaitingForLink;
        private bool _completeRequested;
        private long _messageId = 1;
        private T _messageValue = default!;
        private ITargetBlock<T>? _reservedBy;

        public EnumerableSourceCore(ISourceBlock<T> parent, Action enumerate, TaskScheduler? taskScheduler, CancellationToken cancellationToken)
        {
            this._parent = parent;
            this._cancellationToken = cancellationToken;

            if (taskScheduler == null || taskScheduler == TaskScheduler.Default)
            {
#if NETSTANDARD2_0
                WaitCallback enumerateCb = x => ((Action)x).Invoke();
                this._enumerate = () => ThreadPool.QueueUserWorkItem(enumerateCb, enumerate);

                WaitCallback offerCb = x => ((EnumerableSourceCore<T>)x).OfferToTargets();
                this._offerToTargetsOnTaskScheduler = () => ThreadPool.QueueUserWorkItem(offerCb, this);
#else
                this._enumerate = () => Task.Run(enumerate);

                Action offerAction = this.OfferToTargets;
                this._offerToTargetsOnTaskScheduler = () => Task.Run(offerAction);
#endif
            }
            else
            {
                var taskFactory = new TaskFactory(taskScheduler);
                this._enumerate = () => taskFactory.StartNew(enumerate);

                Action offerAction = this.OfferToTargets;
                this._offerToTargetsOnTaskScheduler = () => taskFactory.StartNew(offerAction);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                this.State = StateEnum.Completed;
                this._completeRequested = true;
                this._tcs.TrySetCanceled(cancellationToken);
            }
            else if (cancellationToken.CanBeCanceled)
            {
                this._cancelReg = cancellationToken.Register(() => this.Complete(false));
            }
        }

        /// <summary>
        /// A lock object to prevent the item being consumed concurrently
        /// </summary>
        private object ItemLock => this._linkManager;

        private object CompletionLock => this._tcs;

        private StateEnum State
        {
            get => (StateEnum)this._state;
            set => this._state = (int)value;
        }

        public Task Completion => this._tcs.Task;

        public void Complete(bool enumerating)
        {
            this._completeRequested = true;

            if (enumerating)
            {
                Debug.Assert(this.State == StateEnum.Enumerating);
                this.CompleteCore();
                return;
            }

            if (this.TransitionAtomically(StateEnum.WaitingForLink, StateEnum.Completed))
                this.CompleteCore();
        }

        public void Fault(Exception exception, bool enumerating)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));

            lock (this._exceptions) this._exceptions.Add(exception);
            this._completeRequested = true;

            if (enumerating)
            {
                Debug.Assert(this.State == StateEnum.Enumerating);
                this.CompleteCore();
                return;
            }

            if (this.TransitionAtomically(StateEnum.WaitingForLink, StateEnum.Completed))
                this.CompleteCore();
        }

        public void OfferItem(T item)
        {
            Debug.Assert(this.State == StateEnum.Enumerating);
            Debug.Assert(this._reservedBy == null);

            lock (this.ItemLock)
            {
                this._messageValue = item;
                this.State = StateEnum.WaitingToOffer;
            }

            this.OfferToTargets();
        }

        public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions? linkOptions)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            linkOptions ??= new DataflowLinkOptions();

            LinkRegistration<T> registration;

            lock (this.CompletionLock)
            {
                if (this.State == StateEnum.Completed)
                {
                    var exception = this.Completion.Exception;
                    if (exception == null)
                        target.Complete();
                    else
                        target.Fault(exception);

                    return ActionDisposable.Nop;
                }

                registration = new LinkRegistration<T>(target, linkOptions.MaxMessages, linkOptions.PropagateCompletion, this.HandleUnlink);
                this._linkManager.AddLink(registration, linkOptions.Append);

                if (this.TransitionAtomically(StateEnum.WaitingForLink, StateEnum.Enumerating))
                    this._enumerate();
            }

            this.OfferToTargetsIfWaiting();

            return new ActionDisposable(registration.Unlink);
        }

        public T ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            T output;

            lock (this.ItemLock)
            {
                if (this.State != StateEnum.WaitingToBeConsumed ||
                    this._messageId != messageHeader.Id ||
                    (this._reservedBy != null && !Equals(this._reservedBy, target)))
                {
                    messageConsumed = false;
                    return default!;
                }

                this._reservedBy = null;
                this._messageId++;
                output = this._messageValue;

                this._linkManager.GetRegistration(target)?.DecrementRemainingMessages();

                this.EnumerateOrComplete();
            }

            messageConsumed = true;
            return output;
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            lock (this.ItemLock)
            {
                switch (this.State)
                {
                    case StateEnum.WaitingForLink:
                    case StateEnum.Enumerating:
                    case StateEnum.Completed:
                        // item is not available
                        return false;
                }

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

            if (this._reservedBy == target && this._messageId == messageHeader.Id)
            {
                this._reservedBy = null;
            }
            else
            {
                throw new InvalidOperationException("The message has not been reserved by the target.");
            }

            this.OfferToTargetsIfWaiting();
        }

        public bool TryReceive(Predicate<T>? filter, out T item)
        {
            item = default!;

            lock (this.ItemLock)
            {
                switch (this.State)
                {
                    case StateEnum.WaitingForLink:
                    case StateEnum.Enumerating:
                    case StateEnum.Completed:
                        // item is not available
                        return false;
                }

                if (this._reservedBy != null) return false;

                var messageValue = this._messageValue;
                if (filter != null && !filter(messageValue))
                    return false;

                item = messageValue;
                this._messageId++;

                this.EnumerateOrComplete();
            }

            return true;
        }

        public bool TryReceiveAll(out IList<T> items)
        {
            var received = this.TryReceive(null, out var item);
            items = received ? new[] { item } : Array.Empty<T>();
            return received;
        }

        private void OfferToTargets()
        {
        StartOffer:
            lock (this.ItemLock)
            {
                if (!this.TransitionAtomically(StateEnum.WaitingToOffer, StateEnum.Offering))
                    return;

                try
                {
                    if (this._reservedBy == null)
                    {
                        var messageHeader = new DataflowMessageHeader(this._messageId);
                        var messageValue = this._messageValue;

                        foreach (var registration in this._linkManager)
                        {
                            var status = registration.Target.OfferMessage(messageHeader, messageValue, this._parent, false);

                            switch (status)
                            {
                                case DataflowMessageStatus.Accepted:
                                    this._messageId++;
                                    registration.DecrementRemainingMessages();
                                    this.EnumerateOrComplete();
                                    goto StartOffer;

                                case DataflowMessageStatus.NotAvailable:
                                    throw new InvalidOperationException("Target cannot return NotAvailable if consumeToAccept is false.");

                                case DataflowMessageStatus.DecliningPermanently:
                                    registration.Unlink();
                                    break;
                            }
                        }
                    }

                    // If the state is not changed, begin waiting.
                    // The state can transition to WaitingToOffer by some methods.
                    if (!this.TransitionAtomically(StateEnum.Offering, StateEnum.WaitingToBeConsumed))
                    {
                        Debug.Assert(this.State == StateEnum.WaitingToOffer);
                    }

                    goto StartOffer;
                }
                catch (Exception ex)
                {
                    lock (this._exceptions) this._exceptions.Add(ex);
                    this.CompleteCore();
                }
            }
        }

        private void OfferToTargetsIfWaiting()
        {
            if (this.TransitionAtomically(StateEnum.WaitingToBeConsumed, StateEnum.WaitingToOffer))
            {
                this._offerToTargetsOnTaskScheduler();
            }
        }

        private void HandleUnlink(LinkRegistration<T> registration)
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
                    this.OfferToTargetsIfWaiting();
                }
            }
        }

        private void EnumerateOrComplete()
        {
            Debug.Assert(this.State == StateEnum.Offering || this.State == StateEnum.WaitingToBeConsumed);

            if (this._completeRequested)
            {
                this.CompleteCore();
            }
            else
            {
                // Lock to avoid to add a link in setting WaitingForLink to State
                lock (this.CompletionLock)
                {
                    if (this._linkManager.Count > 0)
                    {
                        this.State = StateEnum.Enumerating;
                        this._enumerate();
                    }
                    else
                    {
                        this.State = StateEnum.WaitingForLink;
                    }
                }
            }
        }

        private void CompleteCore()
        {
            lock (this.CompletionLock)
            {
                this.State = StateEnum.Completed;

                lock (this._exceptions)
                {
                    if (this._exceptions.Count > 0)
                    {
                        this._tcs.TrySetException(this._exceptions);
                        return;
                    }
                }

                if (this._cancellationToken.IsCancellationRequested)
                {
                    this._tcs.TrySetCanceled(this._cancellationToken);
                }
                else
                {
                    this._tcs.TrySetResult(default);
                }

                var completionTask = this.Completion;
                foreach (var registration in this._linkManager)
                    registration.Complete(completionTask.Exception);
            }

            this._cancelReg.Dispose();
        }

        private bool TransitionAtomically(StateEnum expectedCurrentState, StateEnum destination)
        {
            return Interlocked.CompareExchange(
                ref this._state,
                (int)destination,
                (int)expectedCurrentState
            ) == (int)expectedCurrentState;
        }

        private enum StateEnum
        {
            WaitingForLink,
            Enumerating,
            WaitingToOffer,
            Offering,
            WaitingToBeConsumed,
            Completed,
        }
    }
}
