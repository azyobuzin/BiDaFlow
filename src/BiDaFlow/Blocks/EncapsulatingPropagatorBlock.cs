using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Blocks
{
    internal sealed class EncapsulatingPropagatorBlock<TInput, TOutput>
        : IPropagatorBlock<TInput, TOutput>, IReceivableSourceBlock<TOutput>
    {
        private readonly ITargetBlock<TInput> _entrance;
        private readonly ISourceBlock<TOutput> _terminal;

        public EncapsulatingPropagatorBlock(ITargetBlock<TInput> entrance, ISourceBlock<TOutput> terminal)
        {
            this._entrance = entrance;
            this._terminal = terminal;
        }

        public Task Completion => this._terminal.Completion;

        public void Complete() => this._entrance.Complete();

        public void Fault(Exception exception) => this._entrance.Fault(exception);

        public IDisposable LinkTo(ITargetBlock<TOutput> target, DataflowLinkOptions linkOptions)
            => this._terminal.LinkTo(target, linkOptions);

        public TOutput ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target, out bool messageConsumed)
            => this._terminal.ConsumeMessage(messageHeader, target, out messageConsumed);

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target)
            => this._terminal.ReleaseReservation(messageHeader, target);

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target)
            => this._terminal.ReserveMessage(messageHeader, target);

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, TInput messageValue, ISourceBlock<TInput> source, bool consumeToAccept)
            => this._entrance.OfferMessage(messageHeader, messageValue, new ProxySourceBlock<TInput>(this, source), consumeToAccept);

        public bool TryReceive(Predicate<TOutput>? filter, out TOutput item)
        {
            if (this._terminal is IReceivableSourceBlock<TOutput> r)
                return r.TryReceive(filter, out item);

            item = default!;
            return false;
        }

        public bool TryReceiveAll(out IList<TOutput>? items)
        {
            if (this._terminal is IReceivableSourceBlock<TOutput> r)
                return r.TryReceiveAll(out items);

            items = default;
            return false;
        }
    }
}
