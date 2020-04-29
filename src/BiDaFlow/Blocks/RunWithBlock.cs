using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Blocks
{
    internal sealed class RunWithBlock : IDataflowBlock
    {
        private readonly IDataflowBlock _sourceBlock;
        private readonly IDataflowBlock _targetBlock;

        public RunWithBlock(IDataflowBlock sourceBlock, IDataflowBlock targetBlock)
        {
            this._sourceBlock = sourceBlock;
            this._targetBlock = targetBlock;
        }

        public Task Completion => this._targetBlock.Completion;

        public void Complete() => this._sourceBlock.Complete();

        public void Fault(Exception exception) => this._sourceBlock.Fault(exception);
    }
}
