using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Actors
{
    // TODO: 廃止
    public interface IActor : IDataflowBlock
    {
        ActorEngine Engine { get; }
    }
}
