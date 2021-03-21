using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Fluent;

namespace BiDaFlow.Blocks
{
    internal sealed class ProbePropagatorBlock<T> : IPropagatorBlock<T, T>
    {
        private readonly ISourceBlock<T> _source;
        private readonly ITargetBlock<T> _target;
        private readonly ILinkProbe<T> _probe;

        public ProbePropagatorBlock(ISourceBlock<T> source, ITargetBlock<T> target, ILinkProbe<T> probe)
        {
            this._source = source;
            this._target = target;
            this._probe = probe;
        }

        Task IDataflowBlock.Completion => this._source.Completion;

        void IDataflowBlock.Complete()
        {
            this._probe.OnComplete();
            this._target.Complete();
        }

        void IDataflowBlock.Fault(Exception exception)
        {
            this._probe.OnFault(exception);
            this._target.Fault(exception);
        }

        DataflowMessageStatus ITargetBlock<T>.OfferMessage(DataflowMessageHeader messageHeader, T messageValue, ISourceBlock<T>? source, bool consumeToAccept)
        {
            this._probe.OnOfferMessage(messageHeader, messageValue, consumeToAccept);
            var status = this._target.OfferMessage(messageHeader, messageValue, source == null ? null : this, consumeToAccept);
            this._probe.OnOfferResponse(messageHeader, messageValue, status);
            return status;
        }

        T ISourceBlock<T>.ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target, out bool messageConsumed)
        {
            this._probe.OnConsumeMessage(messageHeader);
            var messageValue = this._source.ConsumeMessage(messageHeader, this, out messageConsumed);
            this._probe.OnConsumeResponse(messageHeader, messageConsumed, messageValue);
            return messageValue;
        }

        bool ISourceBlock<T>.ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            this._probe.OnReserveMessage(messageHeader);
            var reserved = this._source.ReserveMessage(messageHeader, this);
            this._probe.OnReserveResponse(messageHeader, reserved);
            return reserved;
        }

        void ISourceBlock<T>.ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
        {
            this._probe.OnReleaseReservation(messageHeader);
            this._source.ReleaseReservation(messageHeader, this);
        }

        IDisposable ISourceBlock<T>.LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions)
        {
            throw new NotSupportedException();
        }
    }
}
