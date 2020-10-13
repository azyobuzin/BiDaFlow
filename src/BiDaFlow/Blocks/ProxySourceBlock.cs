using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Blocks
{
    /// <summary>
    /// Deflects Target to Source communication.
    /// </summary>
    /// <remarks>
    /// This class is used for the work around for https://github.com/dotnet/runtime/issues/35751
    /// </remarks>
    internal sealed class ProxySourceBlock<T> : ISourceBlock<T>
    {
        private readonly ITargetBlock<T> _targetWrapper;
        private readonly ISourceBlock<T> _source;

        public ProxySourceBlock(ITargetBlock<T> targetWrapper, ISourceBlock<T> source)
        {
            this._targetWrapper = targetWrapper;
            this._source = source;
        }

        public Task Completion => this._source.Completion;

        public void Complete() => this._source.Complete();

        public void Fault(Exception exception) => this._source.Fault(exception);

        public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions)
            => this._source.LinkTo(target, linkOptions);

        public T ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
            => this._source.ConsumeMessage(messageHeader, this._targetWrapper, out messageConsumed);

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
            => this._source.ReleaseReservation(messageHeader, this._targetWrapper);

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
            => this.ReserveMessage(messageHeader, this._targetWrapper);
    }
}
