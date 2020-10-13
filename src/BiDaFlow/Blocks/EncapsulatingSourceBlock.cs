using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Blocks
{
    internal sealed class EncapsulatingSourceBlock<T> : ISourceBlock<T>
    {
        private readonly IDataflowBlock _entrance;
        private readonly ISourceBlock<T> _terminal;

        public EncapsulatingSourceBlock(IDataflowBlock entrance, ISourceBlock<T> terminal)
        {
            this._entrance = entrance;
            this._terminal = terminal;
        }

        public Task Completion => this._terminal.Completion;

        public void Complete() => this._entrance.Complete();

        public void Fault(Exception exception) => this._entrance.Fault(exception);

        public T ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
            => this._terminal.ConsumeMessage(messageHeader, target, out messageConsumed);

        public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions)
            => this._terminal.LinkTo(target, linkOptions);

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
            => this._terminal.ReleaseReservation(messageHeader, target);

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
            => this._terminal.ReserveMessage(messageHeader, target);
    }
}
