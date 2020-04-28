using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Actors
{
    public class Envelope
    {
        internal Actor Address { get; }
        internal Func<Task> Action { get; }

        internal Envelope(Actor address, Func<Task> action)
        {
            this.Address = address;
            this.Action = action;
        }

        public bool Post()
        {
            return this.Address.Engine.Post(this);
        }

        public Task<bool> SendAsync(CancellationToken cancellationToken)
        {
            return this.Address.Engine.SendAsync(this, cancellationToken);
        }

        public Task<bool> SendAsync()
        {
            return this.SendAsync(CancellationToken.None);
        }
    }

    public class EnvelopeWithReply<TReply>
    {
        internal Actor Address { get; }
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
            var envelope = this.HandleReply(tcs);

            if (!envelope.Post())
                tcs.TrySetException(new MessageDeclinedException());

            return tcs.Task;
        }

        public Task<TReply> SendAndReceiveReplyAsync(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<TReply>();
            var envelope = this.HandleReply(tcs);

            envelope.SendAsync(cancellationToken)
                .ContinueWith(
                    t =>
                    {
                        if (t.Exception != null)
                        {
                            tcs.TrySetException(t.Exception.InnerExceptions);
                        }
                        else if (t.IsCanceled)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                tcs.TrySetCanceled(cancellationToken);
                            else
                                tcs.TrySetCanceled();
                        }
                        else if (t.Result == false)
                        {
                            tcs.TrySetException(new MessageDeclinedException());
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
            return this.HandleReply((Action<TReply, Exception?, bool>?)null);
        }

        internal Envelope HandleReply(Action<TReply, Exception?, bool>? replyHandler)
        {
            var handleErrorByReceiver = replyHandler == null || this.HandleErrorByReceiver;

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
                        ReplyCanceled();
                        throw;
                    }

                    ReplyFault(ex);
                    return Task.CompletedTask;
                }

                if (task == null)
                {
                    ReplyCanceled();
                    return Task.CompletedTask;
                }

                return task.ContinueWith(
                    t =>
                    {
                        if (t.Exception != null)
                        {
                            if (handleErrorByReceiver)
                            {
                                ReplyCanceled();
                                return t;
                            }

                            ReplyFault(t.Exception);
                        }
                        else if (t.IsCanceled)
                        {
                            ReplyCanceled();
                        }
                        else
                        {
                            replyHandler?.Invoke(t.Result, null, false);
                        }

                        return Task.CompletedTask;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.DenyChildAttach,
                    TaskScheduler.Default
                ).Unwrap();

                void ReplyCanceled() => replyHandler?.Invoke(default!, null, true);

                void ReplyFault(Exception exception)
                {
                    Debug.Assert(replyHandler != null);
                    replyHandler!(default!, exception, false);
                }
            });
        }

        internal Envelope HandleReply(TaskCompletionSource<TReply> tcs)
        {
            return this.HandleReply((reply, ex, canceled) =>
            {
                if (ex != null)
                {
                    if (ex is AggregateException aex)
                        tcs.TrySetException(aex.InnerExceptions);
                    else
                        tcs.TrySetException(ex);
                }
                else if (canceled)
                {
                    tcs.TrySetCanceled();
                }
                else
                {
                    tcs.TrySetResult(reply);
                }
            });
        }
    }
}
