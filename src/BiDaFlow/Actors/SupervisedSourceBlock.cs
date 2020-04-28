using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Actors
{
    internal sealed class SupervisedSourceBlock<T> : ISourceBlock<T>
    {
        private readonly IDataflowBlock _supervisedBlock;
        private readonly ISourceBlock<T> _sourceBlock;

        public SupervisedSourceBlock(IDataflowBlock supervisedBlock, ISourceBlock<T> sourceBlock)
        {
            this._supervisedBlock = supervisedBlock;
            this._sourceBlock = sourceBlock;
        }

        Task IDataflowBlock.Completion => this._supervisedBlock.Completion;

        void IDataflowBlock.Complete() => this._supervisedBlock.Complete();

        void IDataflowBlock.Fault(Exception exception) => this._supervisedBlock.Fault(exception);

        IDisposable ISourceBlock<T>.LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions)
        {
            return this._sourceBlock.LinkTo(target, linkOptions);
        }

        T ISourceBlock<T>.ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
        {
            return this._sourceBlock.ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        bool ISourceBlock<T>.ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            return this._sourceBlock.ReserveMessage(messageHeader, target);
        }

        void ISourceBlock<T>.ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            this._sourceBlock.ReleaseReservation(messageHeader, target);
        }
    }
}
