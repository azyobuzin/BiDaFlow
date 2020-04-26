using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Internal
{
    internal static class CompletedSourceBlockHolder<T>
    {
        internal static ISourceBlock<T> Instance { get; }

        static CompletedSourceBlockHolder()
        {
            var block = new BufferBlock<T>();
            block.Complete();
            Instance = block;
        }
    }
}
