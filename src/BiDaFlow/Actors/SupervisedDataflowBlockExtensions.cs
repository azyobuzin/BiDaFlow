using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Blocks;

namespace BiDaFlow.Actors
{
    public static class SupervisedDataflowBlockExtensions
    {
        private static readonly ConditionalWeakTable<object, Dictionary<Type, object>> s_sourceBlocks = new ConditionalWeakTable<object, Dictionary<Type, object>>();
        private static readonly ConditionalWeakTable<object, Dictionary<Type, object>> s_targetBlocks = new ConditionalWeakTable<object, Dictionary<Type, object>>();

        public static ISourceBlock<TSource> AsSourceBlock<T, TSource>(this SupervisedDataflowBlock<T> supervisedBlock)
            where T : class, ISourceBlock<TSource>
        {
            throw new NotImplementedException();

            var dic = s_sourceBlocks.GetOrCreateValue(supervisedBlock);

            lock(dic)
            {
                if (dic.TryGetValue(typeof(TSource), out var obj))
                    return (ISourceBlock<TSource>)obj;

                var block = new TransformWithoutBufferBlock<TSource, TSource>(x => x, supervisedBlock._taskScheduler, CancellationToken.None);
            }
        }

        // TODO: AsSourceBlock, AsTargetBlock (and Actor), AsPropagatorBlock
    }
}
