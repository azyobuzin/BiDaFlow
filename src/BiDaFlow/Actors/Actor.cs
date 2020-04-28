using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Blocks;
using BiDaFlow.Internal;

namespace BiDaFlow.Actors
{
    public abstract class Actor : IDataflowBlock
    {
        public Actor(ActorOptions? options)
        {
            this.Engine = new ActorEngine(this, options);
        }

        public Actor() : this(null) { }

        public Task Completion => this.Engine.Completion;

        internal ActorEngine Engine { get; }

        protected virtual void Complete()
        {
            this.Engine.Complete();
        }

        protected virtual void Fault(Exception exception)
        {
            this.Engine.Fault(exception);
        }

        protected internal virtual Task OnCompleted(AggregateException? exception)
        {
            return Task.CompletedTask;
        }

        protected Envelope CreateMessage(Func<Task> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return new Envelope(this, handler);
        }

        protected Envelope CreateMessage(Action handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return new Envelope(this, () =>
            {
                handler();
                return Task.CompletedTask;
            });
        }

        protected EnvelopeWithReply<TReply> CreateMessageWithReply<TReply>(Func<Task<TReply>> handler, bool handleErrorByReceiver = false)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return new EnvelopeWithReply<TReply>(this, handler, handleErrorByReceiver);
        }

        protected EnvelopeWithReply<TReply> CreateMessageWithReply<TReply>(Func<TReply> handler, bool handleErrorByReceiver = false)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return new EnvelopeWithReply<TReply>(this, () => Task.FromResult(handler()), handleErrorByReceiver);
        }

        void IDataflowBlock.Complete() => this.Complete();

        void IDataflowBlock.Fault(Exception exception) => this.Fault(exception);
    }

    public abstract class Actor<TOutput> : Actor, ISourceBlock<TOutput>
    {
        private readonly IPropagatorBlock<TOutput, TOutput> _helperBlock;

        public Actor(ActorOptions? options) : base(options)
        {
            var taskScheduler = options?.TaskScheduler ?? TaskScheduler.Default;
            this._helperBlock = new TransformWithoutBufferBlock<TOutput, TOutput>(IdentityFunc<TOutput>.Instance, taskScheduler, CancellationToken.None);

            this.Completion.ContinueWith(
                (_, state) => ((IDataflowBlock)state).Complete(),
                this._helperBlock,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                taskScheduler
            );
        }

        public Actor() : this(null) { }

        protected Task<bool> SendOutputAsync(TOutput outputValue, CancellationToken cancellationToken = default)
        {
            return this._helperBlock.SendAsync(outputValue, cancellationToken);
        }

        IDisposable ISourceBlock<TOutput>.LinkTo(ITargetBlock<TOutput> target, DataflowLinkOptions linkOptions)
        {
            return this._helperBlock.LinkTo(target, linkOptions);
        }

        TOutput ISourceBlock<TOutput>.ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target, out bool messageConsumed)
        {
            return this._helperBlock.ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        bool ISourceBlock<TOutput>.ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target)
        {
            return this._helperBlock.ReserveMessage(messageHeader, target);
        }

        void ISourceBlock<TOutput>.ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target)
        {
            this._helperBlock.ReleaseReservation(messageHeader, target);
        }
    }
}
