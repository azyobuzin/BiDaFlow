using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Internal;

namespace BiDaFlow.Fluent
{
    public static class DataflowAsyncEnumerable
    {
        public static ISourceBlock<T> AsSourceBlock<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
        {
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));

            var block = new BufferBlock<T>(new DataflowBlockOptions()
            {
                BoundedCapacity = 1,
            });

            Task.Run(async () =>
            {
                try
                {
                    await using (var enumerator = enumerable.GetAsyncEnumerator(cancellationToken))
                    {
                        if (enumerator != null)
                        {
                            while (!cancellationToken.IsCancellationRequested && await enumerator.MoveNextAsync())
                            {
                                var accepted = await block.SendAsync(enumerator.Current, cancellationToken);
                                if (!accepted) return;
                            }
                        }
                    }

                    block.Complete();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    block.Complete();
                }
                catch (Exception ex)
                {
                    ((IDataflowBlock)block).Fault(ex);
                }
            }, cancellationToken);

            return block;
        }

        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this ISourceBlock<T> source)
        {
            return new SourceBlockAsyncEnumerable<T>(source ?? throw new ArgumentNullException(nameof(source)));
        }
    }
}
