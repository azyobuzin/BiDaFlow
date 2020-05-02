using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Blocks
{
    internal sealed class ToTargetBlockBlock<T> : ITargetBlock<T>
    {
        private readonly ITargetBlock<T> _sourceBlock;
        private readonly IDataflowBlock _targetBlock;

        public ToTargetBlockBlock(ITargetBlock<T> sourceBlock, IDataflowBlock targetBlock)
        {
            this._sourceBlock = sourceBlock;
            this._targetBlock = targetBlock;
        }

        public Task Completion => this._targetBlock.Completion;

        public void Complete() => this._sourceBlock.Complete();

        public void Fault(Exception exception) => this._sourceBlock.Fault(exception);

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, T messageValue, ISourceBlock<T> source, bool consumeToAccept)
            => this._sourceBlock.OfferMessage(messageHeader, messageValue, source, consumeToAccept);
    }
}
