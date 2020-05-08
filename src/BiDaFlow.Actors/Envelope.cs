using System;
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

        internal EnvelopeWithReply(Actor address, Func<Task<TReply>> action)
        {
            this.Address = address;
            this.Action = action;
        }

        public Task<TReply> PostAndReceiveReplyAsync()
        {
            var tcs = new TaskCompletionSource<TReply>();
            var cts = new CancellationTokenSource();
            var envelope = this.HandleReply(tcs, cts);

            if (envelope.Post())
            {
                // Throw MessageNeverProcessedException when Address is completed
                _ = this.Address.Completion.ContinueWith(
                    (_, state) => ((TaskCompletionSource<TReply>)state).TrySetException(
                        MessageNeverProcessedException.CreateCompleted()),
                    tcs,
                    cts.Token,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            }
            else
            {
                tcs.TrySetException(MessageNeverProcessedException.CreateDeclined());
            }

            return tcs.Task;
        }

        public Task<TReply> SendAndReceiveReplyAsync(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<TReply>();
            var cts = new CancellationTokenSource();
            var envelope = this.HandleReply(tcs, cts);

            _ = envelope.SendAsync(cancellationToken)
                .ContinueWith(
                    t =>
                    {
                        if (t.IsCanceled && cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(cancellationToken);
                            return;
                        }

                        try
                        {
                            if (t.Result == false)
                            {
                                tcs.TrySetException(MessageNeverProcessedException.CreateDeclined());
                                return;
                            }
                        }
                        catch (AggregateException ex)
                        {
                            tcs.TrySetException(ex.InnerExceptions);
                            return;
                        }

                        // Throw MessageNeverProcessedException when Address is completed
                        _ = this.Address.Completion.ContinueWith(
                            (_, state) => ((TaskCompletionSource<TReply>)state).TrySetException(
                                MessageNeverProcessedException.CreateCompleted()),
                            tcs,
                            cts.Token,
                            TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default
                        );
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
            return this.HandleReply(null);
        }

        internal Envelope HandleReply(Action<TReply, Exception?, bool>? replyHandler)
        {
            if (replyHandler == null)
                return new Envelope(this.Address, this.Action);

            return new Envelope(this.Address, () =>
            {
                Task<TReply> task;
                try
                {
                    task = this.Action();
                }
                catch (Exception ex)
                {
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
                        TReply reply;

                        try
                        {
                            reply = t.Result;
                        }
                        catch (Exception ex)
                        {
                            ReplyFault(ex);
                            return;
                        }

                        replyHandler.Invoke(reply, null, false);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.DenyChildAttach,
                    TaskScheduler.Default
                );

                void ReplyCanceled() => replyHandler(default!, null, true);

                void ReplyFault(Exception exception) => replyHandler(default!, exception, false);
            });
        }

        internal Envelope HandleReply(TaskCompletionSource<TReply> tcs, CancellationTokenSource cts)
        {
            return this.HandleReply((reply, ex, canceled) =>
            {
                cts.Cancel();

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
