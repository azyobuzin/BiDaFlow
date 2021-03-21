using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Reactive.Streams;

namespace BiDaFlow.Internal
{
    internal sealed class PublisherSourceBlock<T> : ISourceBlock<T>, ISubscriber<T>
    {
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenRegistration _cancelReg;
        private readonly TaskCompletionSource<ValueTuple> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly LinkManager<T> _linkManager = new();
        private readonly Queue<T> _queue;
        private readonly TaskSchedulerAutoResetEvent _offerEvent;
        private long _messageId = 1;
        private ITargetBlock<T>? _reservedBy;
        private ISubscription? _upstream;
        private int _freeCount;
        private bool _upstreamCanceled;
        private bool _completeRequested;
        private readonly List<Exception> _exceptions = new();

        public PublisherSourceBlock(int prefetch, TaskScheduler? taskScheduler, CancellationToken cancellationToken)
        {
            this._cancellationToken = cancellationToken;
            this._offerEvent = new TaskSchedulerAutoResetEvent(true, taskScheduler);
            this._freeCount = prefetch;
            this._queue = new Queue<T>(prefetch);

            if (cancellationToken.CanBeCanceled)
            {
                this._cancelReg = cancellationToken.Register(state => ((IDataflowBlock)state).Complete(), this);
            }
        }

        #region IDataflowBlock

        public Task Completion => this._tcs.Task;

        public void Complete()
        {
            this._completeRequested = true;
            this._offerEvent.Set();
        }

        public void Fault(Exception exception)
        {
            this.AddException(exception);
            this.Complete();
        }

        private void AddException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));

            lock (this._exceptions)
                this._exceptions.Add(exception);
        }

        #endregion

        #region ISourceBlock<T>

        IDisposable ISourceBlock<T>.LinkTo(ITargetBlock<T> target, DataflowLinkOptions? linkOptions)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            linkOptions ??= new DataflowLinkOptions();

            LinkRegistration<T> registration;

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

                registration = new LinkRegistration<T>(target, linkOptions.MaxMessages, linkOptions.PropagateCompletion, this.HandleUnlink);
                this._linkManager.AddLink(registration, linkOptions.Append);
            }

            this.OfferToTargets();

            return new ActionDisposable(registration.Unlink);
        }

        T ISourceBlock<T>.ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            T output;

            lock (this.OfferLock)
            {
                if (this._messageId != messageHeader.Id ||
                    (this._reservedBy != null && !Equals(this._reservedBy, target)) ||
                    this._queue.Count == 0)
                {
                    messageConsumed = false;
                    return default!;
                }

                output = this._queue.Dequeue();
                this._reservedBy = null;
                this._messageId++;
                Interlocked.Increment(ref this._freeCount);

                this._linkManager.GetRegistration(target)?.DecrementRemainingMessages();
            }

            this.OfferToTargets();

            messageConsumed = true;
            return output;
        }

        bool ISourceBlock<T>.ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            lock (this.OfferLock)
            {
                if (this._reservedBy == null && this._messageId == messageHeader.Id)
                {
                    this._reservedBy = target;
                    return true;
                }
            }

            return false;
        }

        void ISourceBlock<T>.ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            if (!messageHeader.IsValid) throw new ArgumentException("messageHeader is not valid.");
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (Equals(this._reservedBy, target) && this._messageId == messageHeader.Id)
            {
                this._reservedBy = null;
            }
            else
            {
                throw new InvalidOperationException("The message has not been reserved by the target.");
            }

            this.OfferToTargets();
        }

        #endregion

        #region ISubscriber<T>

        void ISubscriber<T>.OnSubscribe(ISubscription subscription)
        {
            if (this._upstream != null)
                throw new InvalidOperationException();

            this._upstream = subscription;

            if (this._completeRequested)
            {
                this.CompleteCore();
                return;
            }

            // Start worker
            _ = OfferWorkerAsync();
        }

        void ISubscriber<T>.OnNext(T element)
        {
            lock (this.OfferLock)
                this._queue.Enqueue(element);
        }

        void ISubscriber<T>.OnComplete()
        {
            this._upstreamCanceled = true;
            this.Complete();
        }

        void ISubscriber<T>.OnError(Exception cause)
        {
            if (cause == null) throw new ArgumentNullException(nameof(cause));

            this._upstreamCanceled = true;
            this.Fault(cause);
        }

        #endregion

        private object OfferLock => this._tcs;

        private object CompletionLock => this._linkManager;

        private void Request()
        {
            var n = Interlocked.Exchange(ref this._freeCount, 0);
            Debug.Assert(n >= 0);

            if (n > 0) this._upstream!.Request(n);
        }

        private async Task OfferWorkerAsync()
        {
            Debug.Assert(this._upstream != null);

            try
            {
                while (!this._completeRequested)
                {
                    this.Request();

                    await this._offerEvent;

                    if (this._reservedBy != null) continue;

                    lock (this.OfferLock)
                    {
                    StartOffer:
                        if (this._queue.Count > 0)
                        {
                            var messageHeader = new DataflowMessageHeader(this._messageId);
                            var messageValue = this._queue.Peek();

                            foreach (var registration in this._linkManager)
                            {
                                // The item can be reserved in OfferMessage
                                if (this._reservedBy != null) break;

                                if (registration.Unlinked) continue;

                                var status = registration.Target.OfferMessage(messageHeader, messageValue, this, false);

                                switch (status)
                                {
                                    case DataflowMessageStatus.Accepted:
                                        this._queue.Dequeue();

                                        Interlocked.Increment(ref this._freeCount);

                                        this._messageId++;
                                        registration.DecrementRemainingMessages();

                                        goto StartOffer;

                                    case DataflowMessageStatus.NotAvailable:
                                        throw new InvalidOperationException("Target cannot return NotAvailable if consumeToAccept is false.");

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

#pragma warning disable VSTHRD103 // Call async methods when in an async method
            this._cancelReg.Dispose();
#pragma warning restore VSTHRD103

            try
            {
                if (!this._upstreamCanceled)
                {
                    this._upstreamCanceled = true;
                    this._upstream!.Cancel();
                }
            }
            catch (Exception ex)
            {
                this.AddException(ex);
            }

            this.CompleteCore();
        }

        private void OfferToTargets()
        {
            if (this._reservedBy != null || this._queue.Count == 0 || this._linkManager.Count == 0) return;
            this._offerEvent.Set();
        }

        private void CompleteCore()
        {
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

        private void HandleUnlink(LinkRegistration<T> registration)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));

            // Remove from the list of linked targets
            this._linkManager.RemoveLink(registration);

            if (this._linkManager.GetRegistration(registration.Target) == null)
            {
                // Release reservation
                var released = false;
                lock (this.OfferLock)
                {
                    if (Equals(this._reservedBy, registration.Target))
                    {
                        this._reservedBy = null;
                        released = true;
                    }
                }

                if (released)
                {
                    this.OfferToTargets();
                }
            }
        }
    }
}
