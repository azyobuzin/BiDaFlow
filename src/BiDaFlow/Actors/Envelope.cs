using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Actors
{
    public class Envelope
    {
        public Actor Address { get; }
        internal Func<Task> Action { get; }

        internal Envelope(Actor address, Func<Task> action)
        {
            this.Address = address;
            this.Action = action;
        }

        public bool Post()
        {
            return ((IActor)this.Address).Engine.Target.Post(this);
        }

        public Task<bool> SendAsync(CancellationToken cancellationToken)
        {
            return ((IActor)this.Address).Engine.Target.SendAsync(this, cancellationToken);
        }

        public Task<bool> SendAsync()
        {
            return this.SendAsync(CancellationToken.None);
        }
    }

    public class EnvelopeWithReply<TReply>
    {
        public Actor Address { get; }
        internal Func<Task<TReply>> Action { get; }
        internal bool HandleErrorByReceiver { get; }

        internal EnvelopeWithReply(Actor address, Func<Task<TReply>> action, bool handleErrorByReceiver)
        {
            this.Address = address;
            this.Action = action;
            this.HandleErrorByReceiver = handleErrorByReceiver;
        }

        public Task<TReply> PostAndReceiveReplyAsync()
        {
            var tcs = new TaskCompletionSource<TReply>();
            var envelope = this.ToEnvelope(tcs, this.HandleErrorByReceiver);

            if (!envelope.Post())
                tcs.TrySetCanceled();

            return tcs.Task;
        }

        public Task<TReply> SendAndReceiveReplyAsync(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<TReply>();
            var envelope = this.ToEnvelope(tcs, this.HandleErrorByReceiver);

            envelope.SendAsync(cancellationToken)
                .ContinueWith(
                    t =>
                    {
                        if (t.Exception != null)
                        {
                            tcs.TrySetException(t.Exception.InnerExceptions);
                        }
                        else if (t.IsCanceled || t.Result == false)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                tcs.TrySetCanceled(cancellationToken);
                            else
                                tcs.TrySetCanceled();
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.DenyChildAttach,
                    TaskScheduler.Default);

            return tcs.Task;
        }

        public Task<TReply> SendAndReceiveReplyAsync()
        {
            return this.SendAndReceiveReplyAsync(CancellationToken.None);
        }

        public Envelope DiscardReply()
        {
            var tcs = new TaskCompletionSource<TReply>();
            return this.ToEnvelope(tcs, true);
        }

        private Envelope ToEnvelope(TaskCompletionSource<TReply> tcs, bool handleErrorByReceiver)
        {
            return new Envelope(this.Address, () =>
            {
                Task<TReply> task;
                try
                {
                    task = this.Action();
                }
                catch (Exception ex)
                {
                    if (handleErrorByReceiver)
                    {
                        tcs.TrySetCanceled();
                        throw;
                    }

                    tcs.TrySetException(ex);
                    return Task.CompletedTask;
                }

                if (task == null)
                {
                    tcs.TrySetCanceled();
                    return Task.CompletedTask;
                }

                return task.ContinueWith(
                    t =>
                    {
                        if (t.Exception != null)
                        {
                            if (handleErrorByReceiver)
                            {
                                tcs.TrySetCanceled();
                                return t;
                            }

                            tcs.TrySetException(t.Exception.InnerExceptions);
                        }
                        else if (t.IsCanceled)
                        {
                            tcs.TrySetCanceled();
                        }
                        else
                        {
                            tcs.TrySetResult(t.Result);
                        }

                        return Task.CompletedTask;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.DenyChildAttach,
                    TaskScheduler.Default
                ).Unwrap();
            });
        }
    }
}
