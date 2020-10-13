using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Blocks
{
    internal sealed class EncapsulatingTargetBlock<T> : ITargetBlock<T>
    {
        private readonly ITargetBlock<T> _entrance;
        private readonly IDataflowBlock _terminal;

        public EncapsulatingTargetBlock(ITargetBlock<T> entrance, IDataflowBlock terminal)
        {
            this._entrance = entrance;
            this._terminal = terminal;
        }

        public Task Completion => this._terminal.Completion;

        public void Complete() => this._entrance.Complete();

        public void Fault(Exception exception) => this._entrance.Fault(exception);

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, T messageValue, ISourceBlock<T>? source, bool consumeToAccept)
            => this._entrance.OfferMessage(messageHeader, messageValue, source != null ? new ProxySourceBlock<T>(this, source) : null, consumeToAccept);
    }
}
