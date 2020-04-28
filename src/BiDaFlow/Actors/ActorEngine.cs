using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Actors
{
    internal sealed class ActorEngine : ITargetBlock<Envelope?>
    {
        private readonly Actor _actor;
        private readonly ActionBlock<Envelope?> _block;

        public Task Completion { get; }
        public TaskScheduler TaskScheduler { get; }
        public CancellationToken CancellationToken { get; }

        public ActorEngine(Actor actor, ActorOptions? options)
        {
            this._actor = actor;

            options ??= ActorOptions.Default;
            this._block = new ActionBlock<Envelope?>(
                this.HandleEnvelope,
                new ExecutionDataflowBlockOptions()
                {
                    TaskScheduler = options.TaskScheduler,
                    CancellationToken = options.CancellationToken,
                    MaxMessagesPerTask = options.MaxMessagesPerTask,
                    BoundedCapacity = options.BoundedCapacity,
                    NameFormat = options.NameFormat,
                    EnsureOrdered = options.EnsureOrdered,
                    MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                    SingleProducerConstrained = false,
                });

            this.Completion = this._block.Completion
                .ContinueWith(
                    completionTask =>
                    {
                        var onCompletedTask = this._actor.OnCompleted(completionTask.Exception);

                        if (onCompletedTask == null)
                            return Task.FromResult(completionTask);

                        return onCompletedTask.ContinueWith(
                            t =>
                            {
                                if (t.IsFaulted) return t;
                                return completionTask;
                            });
                    },
                    options.TaskScheduler
                )
                .Unwrap().Unwrap();

            this.TaskScheduler = options.TaskScheduler;
            this.CancellationToken = options.CancellationToken;
        }

        private Task? HandleEnvelope(Envelope? envelope)
        {
            if (envelope == null) return null;

            if (!ReferenceEquals(envelope.Address, this._actor))
            {
                this._actor.Fault(new InvalidOperationException("The destination of envelope is not this actor."));
                return null;
            }

            Task? task;

            try
            {
                task = envelope.Action?.Invoke();
            }
            catch (Exception ex)
            {
                this._actor.Fault(ex);
                return null;
            }

            return task?.ContinueWith(
                (t, state) =>
                {
                    var exception = t.Exception;
                    if (exception != null)
                    {
                        ((Actor)state).Fault(exception);
                    }
                },
                this._actor,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                this.TaskScheduler);
        }

        public void CompleteBlock() => this._block.Complete();

        public void FaultBlock(Exception exception) => ((IDataflowBlock)this._block).Fault(exception);

        void IDataflowBlock.Complete() => this._actor.Complete();

        void IDataflowBlock.Fault(Exception exception) => this._actor.Fault(exception);

        DataflowMessageStatus ITargetBlock<Envelope?>.OfferMessage(DataflowMessageHeader messageHeader, Envelope? messageValue, ISourceBlock<Envelope?> source, bool consumeToAccept)
        {
            if (messageValue != null && !ReferenceEquals(messageValue.Address, this._actor))
                return DataflowMessageStatus.Declined;

            return ((ITargetBlock<Envelope?>)this._block).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }
    }
}
