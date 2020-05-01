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

            return new AsyncEnumerableSourceBlock<T>(enumerable, null, cancellationToken);
        }

        public static ISourceBlock<T> AsSourceBlock<T>(this IAsyncEnumerable<T> enumerable, TaskScheduler taskScheduler, CancellationToken cancellationToken)
        {
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));
            if (taskScheduler == null) throw new ArgumentNullException(nameof(taskScheduler));

            return new AsyncEnumerableSourceBlock<T>(enumerable, taskScheduler, cancellationToken);
        }

        public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this ISourceBlock<T> source)
        {
            return new SourceBlockAsyncEnumerable<T>(source ?? throw new ArgumentNullException(nameof(source)));
        }
    }
}
