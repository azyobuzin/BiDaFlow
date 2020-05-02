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
        public static ISourceBlock<TOutput> AsSourceBlock<TOutput>(this IAsyncEnumerable<TOutput> enumerable, CancellationToken cancellationToken = default)
        {
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));

            return new AsyncEnumerableSourceBlock<TOutput>(enumerable, null, cancellationToken);
        }

        public static ISourceBlock<TOutput> AsSourceBlock<TOutput>(this IAsyncEnumerable<TOutput> enumerable, TaskScheduler taskScheduler, CancellationToken cancellationToken)
        {
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));
            if (taskScheduler == null) throw new ArgumentNullException(nameof(taskScheduler));

            return new AsyncEnumerableSourceBlock<TOutput>(enumerable, taskScheduler, cancellationToken);
        }

        public static IAsyncEnumerable<TOutput> AsAsyncEnumerable<TOutput>(this ISourceBlock<TOutput> source)
        {
            return new SourceBlockAsyncEnumerable<TOutput>(source ?? throw new ArgumentNullException(nameof(source)));
        }

        public static IAsyncEnumerable<TOutput> RunThroughDataflowBlock<TInput, TOutput>(
           this IAsyncEnumerable<TInput> source,
           Func<CancellationToken, IPropagatorBlock<TInput, TOutput>> propagatorFactory)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (propagatorFactory == null) throw new ArgumentNullException(nameof(propagatorFactory));

            return new RunThroughAsyncEnumerable<TInput, TOutput>(source, propagatorFactory);
        }

        public static IAsyncEnumerable<TOutput> RunThroughDataflowBlock<TInput, TOutput>(
            this IAsyncEnumerable<TInput> source,
            Func<IPropagatorBlock<TInput, TOutput>> propagatorFactory)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (propagatorFactory == null) throw new ArgumentNullException(nameof(propagatorFactory));

            return new RunThroughAsyncEnumerable<TInput, TOutput>(source, _ => propagatorFactory());
        }
    }
}
