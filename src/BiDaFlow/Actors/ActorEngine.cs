using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Actors
{
    public sealed class ActorEngine
    {
        private readonly EnvelopeTarget _target;

        internal ActorEngine(Actor parent, ActorOptions? options)
        {
            this._target = new EnvelopeTarget(parent, options);
        }

        internal ITargetBlock<Envelope?> Target => this._target;

        internal TaskScheduler TaskScheduler => this._target.TaskScheduler;

        internal CancellationToken CancellationToken => this._target.CancellationToken;

        private sealed class EnvelopeTarget : ITargetBlock<Envelope?>
        {
            private readonly Actor _actor;
            private readonly ActionBlock<Envelope?> _block;
            private readonly Task _completion;
            internal TaskScheduler TaskScheduler { get; }
            internal CancellationToken CancellationToken { get; }

            internal EnvelopeTarget(Actor actor, ActorOptions? options)
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

                this._completion = this._block.Completion
                    .ContinueWith(
                        async t =>
                        {
                            await this._actor.OnCompleted(t.Exception).ConfigureAwait(false);
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

            Task IDataflowBlock.Completion => this._completion;

            void IDataflowBlock.Complete() => this._block.Complete();

            void IDataflowBlock.Fault(Exception exception) => ((IDataflowBlock)this._block).Fault(exception);

            DataflowMessageStatus ITargetBlock<Envelope?>.OfferMessage(DataflowMessageHeader messageHeader, Envelope? messageValue, ISourceBlock<Envelope?> source, bool consumeToAccept)
            {
                if (messageValue != null && !ReferenceEquals(messageValue.Address, this._actor))
                    return DataflowMessageStatus.Declined;

                return ((ITargetBlock<Envelope?>)this._block).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
            }
        }
    }
}
