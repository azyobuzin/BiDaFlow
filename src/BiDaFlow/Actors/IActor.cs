using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Actors
{
    public interface IActor : IDataflowBlock
    {
        ActorEngine Engine { get; }
    }
}
