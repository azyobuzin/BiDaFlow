using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Blocks
{
    internal sealed class EncapsulatingDataflowBlock : IDataflowBlock
    {
        private readonly IDataflowBlock _entrace;
        private readonly IDataflowBlock _terminal;

        public EncapsulatingDataflowBlock(IDataflowBlock entrance, IDataflowBlock terminal)
        {
            this._entrace = entrance;
            this._terminal = terminal;
        }

        public Task Completion => this._terminal.Completion;

        public void Complete() => this._entrace.Complete();

        public void Fault(Exception exception) => this._entrace.Fault(exception);
    }
}
