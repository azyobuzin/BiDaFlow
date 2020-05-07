using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Internal;
using Reactive.Streams;

namespace BiDaFlow.Fluent
{
    public static class DataflowReactiveStreams
    {
        public static ISourceBlock<TOutput> AsSourceBlock<TOutput>(this IPublisher<TOutput> source, int prefetch, CancellationToken cancellationToken = default)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (prefetch < 1) throw new ArgumentOutOfRangeException(nameof(prefetch), "prefetch cannot be less than 1.");

            throw new NotImplementedException();
        }

        public static ITargetBlock<TInput> ToTargetBlock<TInput>(this ISubscriber<TInput> subscriber)
        {
            if (subscriber == null) throw new ArgumentNullException(nameof(subscriber));

            throw new NotImplementedException();
        }

        public static IPropagatorBlock<TInput, TOutput> ToPropagatorBlock<TInput, TOutput>(
            this IProcessor<TInput, TOutput> processor,
            int prefetch,
            CancellationToken cancellationToken = default)
        {
            if (processor == null) throw new ArgumentNullException(nameof(processor));

            throw new NotImplementedException();
        }

        public static IPublisher<TOutput> AsPublisher<TOutput>(this ISourceBlock<TOutput> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            throw new NotImplementedException();
        }

        public static ISubscriber<TInput> AsSubscriber<TInput>(this ITargetBlock<TInput> target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            throw new NotImplementedException();
        }

        public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IPublisher<T> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return new PublisherAsyncEnumerable<T>(source);
        }

        public static IPublisher<T> AsPublisher<T>(this IAsyncEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return new AsyncEnumerablePublisher<T>(source);
        }

        public static IPublisher<TOutput> RunThroughDataflowBlock<TInput, TOutput>(
            this IPublisher<TInput> source,
            int prefetch,
            Func<CancellationToken, IPropagatorBlock<TInput, TOutput>> propagatorFactory)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (prefetch < 1) throw new ArgumentOutOfRangeException(nameof(prefetch), "prefetch cannot be less than 1.");
            if (propagatorFactory == null) throw new ArgumentNullException(nameof(propagatorFactory));

            throw new NotImplementedException();
        }

        public static IPublisher<TOutput> RunThroughDataflowBlock<TInput, TOutput>(
            this IPublisher<TInput> source,
            int prefetch,
            Func<IPropagatorBlock<TInput, TOutput>> propagatorFactory)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (prefetch < 1) throw new ArgumentOutOfRangeException(nameof(prefetch), "prefetch cannot be less than 1.");
            if (propagatorFactory == null) throw new ArgumentNullException(nameof(propagatorFactory));

            throw new NotImplementedException();
        }
    }
}
