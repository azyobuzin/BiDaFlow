using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Actors
{
    internal sealed class SupervisedTargetBlock<T> : ITargetBlock<T>
    {
        private readonly IDataflowBlock _supervisedBlock;
        private readonly ITargetBlock<T> _targetBlock;

        public SupervisedTargetBlock(IDataflowBlock supervisedBlock, ITargetBlock<T> targetBlock)
        {
            this._supervisedBlock = supervisedBlock;
            this._targetBlock = targetBlock;
        }

        Task IDataflowBlock.Completion => this._supervisedBlock.Completion;

        void IDataflowBlock.Complete() => this._supervisedBlock.Complete();

        void IDataflowBlock.Fault(Exception exception) => this._supervisedBlock.Fault(exception);

        DataflowMessageStatus ITargetBlock<T>.OfferMessage(DataflowMessageHeader messageHeader, T messageValue, ISourceBlock<T> source, bool consumeToAccept)
        {
            return this._targetBlock.OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }
    }
}
