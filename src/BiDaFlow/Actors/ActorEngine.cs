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
                    async t =>
                    {
                        var onCompletedTask = this._actor.OnCompleted(t.Exception);
                        if (onCompletedTask != null)
                            await onCompletedTask.ConfigureAwait(false);

                        return t;
                    },
                    options.TaskScheduler
                )
                .Unwrap().Unwrap();

            this.TaskScheduler = options.TaskScheduler;
            this.CancellationToken = options.CancellationToken;
        }

        private Task HandleEnvelope(Envelope? envelope)
        {
            if (envelope == null) return Task.CompletedTask;

            if (!ReferenceEquals(envelope.Address, this._actor))
                throw new ArgumentException("The destination of envelope is not this actor.");

            return envelope.Action?.Invoke() ?? Task.CompletedTask;
        }

        public void Complete() => this._block.Complete();

        public void Fault(Exception exception) => ((IDataflowBlock)this._block).Fault(exception);

        DataflowMessageStatus ITargetBlock<Envelope?>.OfferMessage(DataflowMessageHeader messageHeader, Envelope? messageValue, ISourceBlock<Envelope?> source, bool consumeToAccept)
        {
            if (messageValue != null && !ReferenceEquals(messageValue.Address, this._actor))
                return DataflowMessageStatus.Declined;

            return ((ITargetBlock<Envelope?>)this._block).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }
    }
}
