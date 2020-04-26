using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Actors
{
    public class ActorOptions : ExecutionDataflowBlockOptions
    {
        internal static readonly ActorOptions Default = new ActorOptions();
    }
}
