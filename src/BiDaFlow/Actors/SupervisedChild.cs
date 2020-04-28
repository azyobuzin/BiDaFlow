using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Actors
{
    internal sealed class SupervisedChild
    {
        public IDataflowBlock Block { get; }

        /// <seealso cref="SupervisionOptions.KillWhenCompleted" />
        public bool KillWhenCompleted { get; }

        /// <seealso cref="SupervisionOptions.WaitCompletion" />
        public bool WaitCompletion { get; }

        public SupervisedChild(IDataflowBlock block, bool killWhenCompleted, bool waitCompletion)
        {
            this.Block = block;
            this.KillWhenCompleted = killWhenCompleted;
            this.WaitCompletion = waitCompletion;
        }
    }
}
