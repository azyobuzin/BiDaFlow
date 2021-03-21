using System;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Fluent
{
    public interface ILinkProbe<T>
    {
        void Initialize(ISourceBlock<T> source, ITargetBlock<T> target, DataflowLinkOptions linkOptions)
#if NETSTANDARD2_1
        { }
#else
        ;
#endif

        void OnComplete()
#if NETSTANDARD2_1
        { }
#else
        ;
#endif

        void OnFault(Exception exception)
#if NETSTANDARD2_1
        { }
#else
        ;
#endif

        void OnOfferMessage(DataflowMessageHeader messageHeader, T messageValue, bool consumeToAccept)
#if NETSTANDARD2_1
        { }
#else
        ;
#endif

        void OnOfferResponse(DataflowMessageHeader messageHeader, T messageValue, DataflowMessageStatus status)
#if NETSTANDARD2_1
        { }
#else
        ;
#endif

        void OnConsumeMessage(DataflowMessageHeader messageHeader)
#if NETSTANDARD2_1
        { }
#else
        ;
#endif

        void OnConsumeResponse(DataflowMessageHeader messageHeader, bool messageConsumed, T? messageValue)
#if NETSTANDARD2_1
        { }
#else
        ;
#endif

        void OnReserveMessage(DataflowMessageHeader messageHeader)
#if NETSTANDARD2_1
        { }
#else
        ;
#endif

        void OnReserveResponse(DataflowMessageHeader messageHeader, bool reserved)
#if NETSTANDARD2_1
        { }
#else
        ;
#endif

        void OnReleaseReservation(DataflowMessageHeader messageHeader)
#if NETSTANDARD2_1
        { }
#else
        ;
#endif

        void OnUnlink()
#if NETSTANDARD2_1
        { }
#else
        ;
#endif
    }
}
