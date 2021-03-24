using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Internal;

namespace BiDaFlow.Fluent
{
    public static class DataflowAsyncEnumerable
    {
        /// <exception cref="ArgumentNullException"><paramref name="enumerable"/> is <see langword="null"/>.</exception>
        public static ISourceBlock<TOutput> AsSourceBlock<TOutput>(this IAsyncEnumerable<TOutput> enumerable, CancellationToken cancellationToken = default)
        {
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));

            return new AsyncEnumerableSourceBlock<TOutput>(enumerable, null, cancellationToken);
        }

        /// <exception cref="ArgumentNullException"><paramref name="enumerable"/> or <paramref name="taskScheduler"/> is <see langword="null"/>.</exception>
        public static ISourceBlock<TOutput> AsSourceBlock<TOutput>(this IAsyncEnumerable<TOutput> enumerable, TaskScheduler taskScheduler, CancellationToken cancellationToken)
        {
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));
            if (taskScheduler == null) throw new ArgumentNullException(nameof(taskScheduler));

            return new AsyncEnumerableSourceBlock<TOutput>(enumerable, taskScheduler, cancellationToken);
        }

        /// <summary>
        /// Creates an <see cref="IAsyncEnumerable{T}"/> to consume the items from <see cref="ISourceBlock{TOutput}"/>.
        /// </summary>
        /// <typeparam name="TOutput">Specifies the type of data contained in the source.</typeparam>
        /// <param name="source">The source to wrap.</param>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// The returned <see cref="IAsyncEnumerable{T}"/> behaves following:
        /// <list type="number">
        ///     <item><description>
        ///         Makes a link from <paramref name="source"/> to the internal target block
        ///         when <see cref="IAsyncEnumerable{T}.GetAsyncEnumerator(CancellationToken)"/> is called.
        ///     </description></item>
        ///     <item><description>
        ///         Consumes an element of <paramref name="source"/> when <see cref="IAsyncEnumerator{T}.MoveNextAsync"/> is called.
        ///     </description></item>
        /// </list>
        /// </para>
        /// <para>
        /// Offers received when the user is not awaiting <see cref="IAsyncEnumerator{T}.MoveNextAsync"/> will be postponed
        /// because the internal target block doesn't have a buffer.
        /// </para>
        /// </remarks>
        [SuppressMessage("Style", "VSTHRD200:Use Async naming convention")]
        public static IAsyncEnumerable<TOutput> AsAsyncEnumerable<TOutput>(this ISourceBlock<TOutput> source)
        {
            return new SourceBlockAsyncEnumerable<TOutput>(source ?? throw new ArgumentNullException(nameof(source)));
        }

        /// <summary>
        /// Creates an <see cref="IAsyncEnumerable{T}"/> that transforms the elements from <paramref name="source"/> with
        /// <see cref="IPropagatorBlock{TInput, TOutput}"/>.
        /// </summary>
        /// <typeparam name="TInput">The type of the elements in the source sequence.</typeparam>
        /// <typeparam name="TOutput">The type of the elements in the result sequence</typeparam>
        /// <param name="source">A sequence of elements that will be transformed.</param>
        /// <param name="propagatorFactory">
        /// <para>A function that creates <see cref="IPropagatorBlock{TInput, TOutput}"/> used to transform the elements.</para>
        /// <para>This function is called each time <see cref="IAsyncEnumerable{T}.GetAsyncEnumerator(CancellationToken)"/> is called.</para>
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> or <paramref name="propagatorFactory"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// If the dataflow block instantiated in <paramref name="propagatorFactory"/> will not be used somewhere else,
        /// you can set <see langword="true"/> to <see cref="ExecutionDataflowBlockOptions.SingleProducerConstrained"/>
        /// of the options object for the block.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code language="csharp"><![CDATA[
        /// await AsyncEnumerable.Range(1, 100)
        ///     // Process elements in parallel with IPropagatorBlock
        ///     .RunThroughDataflowBlock(cancellationToken =>
        ///         new TransformBlock<int, int>(
        ///             x => x * 10,
        ///             new ExecutionDataflowBlockOptions
        ///             {
        ///                 CancellationToken = cancellationToken,
        ///                 BoundedCapacity = 6,
        ///                 MaxDegreeOfParallelism = 4,
        ///                 SingleProducerConstrained = true,
        ///             })
        ///     )
        ///     // Subsequent process can be written with System.Linq.Async
        ///     .ForEachAsync(x => Console.WriteLine(x));
        /// ]]></code>
        /// </example>
        [SuppressMessage("Style", "VSTHRD200:Use Async naming convention")]
        public static IAsyncEnumerable<TOutput> RunThroughDataflowBlock<TInput, TOutput>(
           this IAsyncEnumerable<TInput> source,
           Func<CancellationToken, IPropagatorBlock<TInput, TOutput>> propagatorFactory)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (propagatorFactory == null) throw new ArgumentNullException(nameof(propagatorFactory));

            return new RunThroughAsyncEnumerable<TInput, TOutput>(source, propagatorFactory);
        }

        /// <inheritdoc cref="RunThroughDataflowBlock{TInput, TOutput}(IAsyncEnumerable{TInput}, Func{CancellationToken, IPropagatorBlock{TInput, TOutput}})"/>
        [SuppressMessage("Style", "VSTHRD200:Use Async naming convention")]
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
