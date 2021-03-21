using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Blocks;
using BiDaFlow.Internal;

namespace BiDaFlow.Fluent
{
    public static class FluentDataflow
    {
        private static readonly DataflowLinkOptions s_defaultOptions = new();
        private static readonly DataflowLinkOptions s_propagateCompletionOptions = new() { PropagateCompletion = true };

        /// <inheritdoc cref="PropagateCompletion(IDataflowBlock, IDataflowBlock, WhenPropagate)"/>
        public static IDisposable PropagateCompletion(this IDataflowBlock source, IDataflowBlock target)
        {
            return source.PropagateCompletion(target, WhenPropagate.Both);
        }

        /// <summary>
        /// Links <paramref name="source"/> to <paramref name="target"/> to notify completion of <paramref name="source"/> to <paramref name="target"/>.
        /// </summary>
        /// <returns>An <see cref="IDisposable"/> to unlink the source from the target.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> or <paramref name="target"/> is <see langword="null"/>.
        /// </exception>
        public static IDisposable PropagateCompletion(this IDataflowBlock source, IDataflowBlock target, WhenPropagate when)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            if ((when & (WhenPropagate.Both)) == 0) return ActionDisposable.Nop;

            var task = source.Completion;

            if (task.IsCompleted)
            {
                SendCompletion();
                return ActionDisposable.Nop;
            }

            var cts = new CancellationTokenSource();

            _ = task.ContinueWith(
                _ =>
                {
                    if (!cts.IsCancellationRequested)
                        SendCompletion();
                },
                cts.Token,
                TaskContinuationOptions.None,
                TaskScheduler.Default
            );

            return new ActionDisposable(cts.Cancel);

            void SendCompletion()
            {
                var exception = task.Exception;
                if (exception == null)
                {
                    if ((when & WhenPropagate.Complete) != 0)
                        target.Complete();
                }
                else
                {
                    if ((when & WhenPropagate.Fault) != 0)
                        target.Fault(exception);
                }
            }
        }

        /// <summary>
        /// Links the <see cref="ISourceBlock{TOutput}"/> to the specified <see cref="ITargetBlock{TInput}"/> propagating completion.
        /// </summary>
        /// <remarks>
        /// This method is the same as <c>DataflowBlock.LinkTo(source, target, new DataflowLinkOptions() { PropagateCompletion = true })</c>.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> or <paramref name="target"/> is <see langword="null"/>.
        /// </exception>
        /// <seealso cref="DataflowBlock.LinkTo{TOutput}(ISourceBlock{TOutput}, ITargetBlock{TOutput})"/>
        public static IDisposable LinkWithCompletion<TOutput>(this ISourceBlock<TOutput> source, ITargetBlock<TOutput> target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            return source.LinkTo(target, s_propagateCompletionOptions);
        }

        /// <summary>
        /// Returns a <see cref="ISourceBlock{TOutput}"/> that has been completed.
        /// </summary>
        public static ISourceBlock<TOutput> CompletedSourceBlock<TOutput>()
        {
            return CompletedSourceBlockHolder<TOutput>.Instance;
        }

        /// <exception cref="ArgumentNullException"><paramref name="enumerable"/> is <see langword="null"/>.</exception>
        public static ISourceBlock<TOutput> AsSourceBlock<TOutput>(this IEnumerable<TOutput> enumerable, CancellationToken cancellationToken = default)
        {
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));

            return new EnumerableSourceBlock<TOutput>(enumerable, null, cancellationToken);
        }

        /// <exception cref="ArgumentNullException"><paramref name="enumerable"/> or <paramref name="taskScheduler"/> is <see langword="null"/>.</exception>
        public static ISourceBlock<TOutput> AsSourceBlock<TOutput>(this IEnumerable<TOutput> enumerable, TaskScheduler taskScheduler, CancellationToken cancellationToken)
        {
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));
            if (taskScheduler == null) throw new ArgumentNullException(nameof(taskScheduler));

            return new EnumerableSourceBlock<TOutput>(enumerable, taskScheduler, cancellationToken);
        }

        /// <exception cref="ArgumentNullException"><paramref name="enumerator"/> is <see langword="null"/>.</exception>
        public static ISourceBlock<TOutput> ToSourceBlock<TOutput>(this IEnumerator<TOutput> enumerator, CancellationToken cancellationToken = default)
        {
            if (enumerator == null) throw new ArgumentNullException(nameof(enumerator));

            return new EnumerableSourceBlock<TOutput>(enumerator, null, cancellationToken);
        }

        /// <exception cref="ArgumentNullException"><paramref name="enumerator"/> or <paramref name="taskScheduler"/> is <see langword="null"/>.</exception>
        public static ISourceBlock<TOutput> ToSourceBlock<TOutput>(this IEnumerator<TOutput> enumerator, TaskScheduler taskScheduler, CancellationToken cancellationToken)
        {
            if (enumerator == null) throw new ArgumentNullException(nameof(enumerator));
            if (taskScheduler == null) throw new ArgumentNullException(nameof(taskScheduler));

            return new EnumerableSourceBlock<TOutput>(enumerator, taskScheduler, cancellationToken);
        }

        /// <summary>
        /// Creates a new <see cref="IObserver{T}"/> abstraction over the <see cref="ITargetBlock{TInput}"/>.
        /// This method drops overflow items in contrast to <seealso cref="DataflowBlock.AsObserver{TInput}(ITargetBlock{TInput})"/>,
        /// which buffers overflow items with <see cref="DataflowBlock.SendAsync{TInput}(ITargetBlock{TInput}, TInput)"/>.
        /// </summary>
        /// <typeparam name="TInput">Specifies the type of input accepted by the target block.</typeparam>
        /// <param name="target">The target to wrap</param>
        /// <returns>An oberver that wraps the target block.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
        public static IObserver<TInput> AsObserverDroppingOverflowItems<TInput>(this ITargetBlock<TInput> target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return new DroppingObserver<TInput>(target);
        }

        /// <summary>
        /// Encapsulates a target and a block into a single target.
        /// </summary>
        /// <param name="entrance">The target block accepts inputs and completion.</param>
        /// <param name="terminal">The block that represents the terminal of the block chain.</param>
        /// <exception cref="ArgumentNullException"><paramref name="entrance"/> or <paramref name="terminal"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <see cref="IDataflowBlock.Completion"/> property of the return value returns <c>Completion</c> of <paramref name="terminal"/>.
        /// Other methods of the return value call the methods of <paramref name="entrance"/>.
        /// </remarks>
        public static ITargetBlock<TInput> EncapsulateAsTargetBlock<TInput>(ITargetBlock<TInput> entrance, IDataflowBlock terminal)
        {
            if (entrance == null) throw new ArgumentNullException(nameof(entrance));
            if (terminal == null) throw new ArgumentNullException(nameof(terminal));

            return new EncapsulatingTargetBlock<TInput>(entrance, terminal);
        }

        /// <summary>
        /// Encapsulates a block and a source into a single source.
        /// </summary>
        /// <param name="entrance">The block that represents the entrace of the block chain and accepts completion.</param>
        /// <param name="terminal">The source block that yields outputs.</param>
        /// <exception cref="ArgumentNullException"><paramref name="entrance"/> or <paramref name="terminal"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <see cref="IDataflowBlock.Complete"/> and <see cref="IDataflowBlock.Fault(Exception)"/> of the return value call the methods of <paramref name="entrance"/>.
        /// Other members of the return value call the methods of <paramref name="terminal"/>
        /// </remarks>
        public static ISourceBlock<TOutput> EncapsulateAsSourceBlock<TOutput>(IDataflowBlock entrance, ISourceBlock<TOutput> terminal)
        {
            if (entrance == null) throw new ArgumentNullException(nameof(entrance));
            if (terminal == null) throw new ArgumentNullException(nameof(terminal));

            return new EncapsulatingSourceBlock<TOutput>(entrance, terminal);
        }

        /// <summary>
        /// Encapsulates a target and a source into a single propagator.
        /// </summary>
        /// <param name="entrance">The target block accepts inputs and completion.</param>
        /// <param name="terminal">The source block that yields outputs.</param>
        /// <exception cref="ArgumentNullException"><paramref name="entrance"/> or <paramref name="terminal"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// This method works the same way as <see cref="DataflowBlock.Encapsulate{TInput, TOutput}(ITargetBlock{TInput}, ISourceBlock{TOutput})"/>.
        /// But this method works around <a href="https://github.com/dotnet/runtime/issues/35751">a bug of <c>DataflowBlock.Encapsulate</c></a>.
        /// </remarks>
        public static IPropagatorBlock<TInput, TOutput> EncapsulateAsPropagatorBlock<TInput, TOutput>(ITargetBlock<TInput> entrance, ISourceBlock<TOutput> terminal)
        {
            if (entrance == null) throw new ArgumentNullException(nameof(entrance));
            if (terminal == null) throw new ArgumentNullException(nameof(terminal));

            return new EncapsulatingPropagatorBlock<TInput, TOutput>(entrance, terminal);
        }

        /// <summary>
        /// Encapsulates two blocks into a single block.
        /// </summary>
        /// <param name="entrance">The block that represents the entrace of the block chain and accepts completion.</param>
        /// <param name="terminal">The block that represents the terminal of the block chain.</param>
        /// <exception cref="ArgumentNullException"><paramref name="entrance"/> or <paramref name="terminal"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <see cref="IDataflowBlock.Completion"/> property of the return value returns <c>Completion</c> of <paramref name="terminal"/>.
        /// <see cref="IDataflowBlock.Complete"/> and <see cref="IDataflowBlock.Fault(Exception)"/> of the return value call the methods of <paramref name="entrance"/>.
        /// </remarks>
        public static IDataflowBlock EncapsulateAsDataflowBlock(IDataflowBlock entrance, IDataflowBlock terminal)
        {
            if (entrance == null) throw new ArgumentNullException(nameof(entrance));
            if (terminal == null) throw new ArgumentNullException(nameof(terminal));

            return new EncapsulatingDataflowBlock(entrance, terminal);
        }

        /// <summary>
        /// Makes a link from <paramref name="source"/> to <paramref name="follower"/> and returns an encapsulated block.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="follower"/> is <see langword="null"/>.</exception>
        public static IPropagatorBlock<TInput, TOutput> Chain<TInput, TSourceOutput, TOutput>(
            this IPropagatorBlock<TInput, TSourceOutput> source,
            IPropagatorBlock<TSourceOutput, TOutput> follower)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (follower == null) throw new ArgumentNullException(nameof(follower));

            source.LinkWithCompletion(follower);
            return EncapsulateAsPropagatorBlock(source, follower);
        }

        /// <summary>
        /// Makes a link from <paramref name="source"/> to <paramref name="target"/> and returns an encapsulated target block.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="target"/> is <see langword="null"/>.</exception>
        public static ITargetBlock<TInput> ChainToTarget<TInput, TOutput>(this IPropagatorBlock<TInput, TOutput> source, ITargetBlock<TOutput> target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            source.LinkWithCompletion(target);
            return EncapsulateAsTargetBlock(source, target);
        }

        /// <summary>
        /// Makes a link from <paramref name="source"/> to <paramref name="target"/> and returns an encapsulated block.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="target"/> is <see langword="null"/>.</exception>
        public static IDataflowBlock RunWith<T>(this ISourceBlock<T> source, ITargetBlock<T> target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            source.LinkWithCompletion(target);
            return EncapsulateAsDataflowBlock(source, target);
        }

        /// <summary>
        /// Merges the specified source blocks. The returned block will be completed when all of the source blocks are completed.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="sources"/> is <see langword="null"/>.</exception>
        public static ISourceBlock<TOutput> Merge<TOutput>(IEnumerable<ISourceBlock<TOutput>> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            var sourceList = sources.ToList();
            sourceList.RemoveAll(x =>
                x?.Completion.Status switch
                {
                    null => true,
                    TaskStatus.RanToCompletion => true,
                    TaskStatus.Canceled => true,
                    _ => false,
                }
            );

            var workingCount = sourceList.Count;
            if (workingCount == 0) return CompletedSourceBlock<TOutput>();

            var resultBlock = new TransformWithoutBufferBlock<TOutput, TOutput>(IdentityFunc<TOutput>.Instance);

            foreach (var source in sourceList)
            {
                source.LinkTo(resultBlock);

                _ = source.Completion.ContinueWith(
                    t =>
                    {
                        var newWorkingCount = Interlocked.Decrement(ref workingCount);

                        var exception = t.Exception;
                        if (exception != null)
                        {
                            ((IDataflowBlock)resultBlock).Fault(exception);
                        }
                        else if (newWorkingCount == 0)
                        {
                            resultBlock.Complete();
                        }
                    },
                    TaskScheduler.Default);
            }

            return resultBlock;
        }

        /// <inheritdoc cref="Merge{TOutput}(IEnumerable{ISourceBlock{TOutput}})"/>
        public static ISourceBlock<TOutput> Merge<TOutput>(params ISourceBlock<TOutput>[] sources)
        {
            return Merge((IEnumerable<ISourceBlock<TOutput>>)sources);
        }

        /// <summary>
        /// Merges the specified source blocks and returns a propagator block that has the same interface as <paramref name="propagator"/>.
        /// The returned block will be completed when all of the source blocks are completed.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="propagator"/> or <paramref name="sources"/> is <see langword="null"/>.</exception>
        public static IPropagatorBlock<TInput, TOutput> Merge<TInput, TOutput>(this IPropagatorBlock<TInput, TOutput> propagator, IEnumerable<ISourceBlock<TOutput>> sources)
        {
            if (propagator == null) throw new ArgumentNullException(nameof(propagator));
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            var mergedSource = Merge(new[] { propagator }.Concat(sources));
            return EncapsulateAsPropagatorBlock(propagator, mergedSource);
        }

        /// <inheritdoc cref="Merge{TInput, TOutput}(IPropagatorBlock{TInput, TOutput}, IEnumerable{ISourceBlock{TOutput}})"/>
        public static IPropagatorBlock<TInput, TOutput> Merge<TInput, TOutput>(this IPropagatorBlock<TInput, TOutput> propagator, params ISourceBlock<TOutput>[] sources)
        {
            return propagator.Merge((IEnumerable<ISourceBlock<TOutput>>)sources);
        }

        /// <exception cref="ArgumentNullException"><paramref name="source"/>, <paramref name="target"/>, <paramref name="linkOptions"/> or <paramref name="probe"/> is <see langword="null"/>.</exception>
        public static IDisposable LinkWithProbe<T>(this ISourceBlock<T> source, ITargetBlock<T> target, DataflowLinkOptions linkOptions, ILinkProbe<T> probe)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (linkOptions == null) throw new ArgumentNullException(nameof(linkOptions));
            if (probe == null) throw new ArgumentNullException(nameof(probe));

            probe.Initialize(source, target, linkOptions);
            var unlinker = source.LinkTo(new ProbePropagatorBlock<T>(source, target, probe), linkOptions);

            return new ActionDisposable(() =>
            {
                probe.OnUnlink();
                unlinker.Dispose();
            });
        }

        /// <exception cref="ArgumentNullException"><paramref name="source"/>, <paramref name="target"/> or <paramref name="probe"/> is <see langword="null"/>.</exception>
        /// <example>
        /// <code><![CDATA[
        /// IDisposable unlinker = source.LinkWithProbe(target, new ConsoleProbe<T>("ExampleLink"));
        /// 
        /// class ConsoleProbe<T> : ILinkProbe<T>
        /// {
        ///     public string Name { get; }
        /// 
        ///     public ConsoleProbe(string name) => this.Name = name;
        /// 
        ///     void ILinkProbe<T>.OnComplete()
        ///     {
        ///         Console.WriteLine("{0}: -> Complete", this.Name);
        ///     }
        /// 
        ///     void ILinkProbe<T>.OnFault(Exception exception)
        ///     {
        ///         Console.WriteLine("{0}: -> Fault({1})", this.Name, exception.GetType().Name);
        ///     }
        /// 
        ///     void ILinkProbe<T>.OnOfferResponse(DataflowMessageHeader messageHeader, T messageValue, DataflowMessageStatus status)
        ///     {
        ///         Console.WriteLine("{0}: -> OfferMessage({1}), <- {2}", this.Name, messageHeader.Id, status);
        ///     }
        /// 
        ///     void ILinkProbe<T>.OnConsumeResponse(DataflowMessageHeader messageHeader, bool messageConsumed, T? messageValue)
        ///     {
        ///         Console.WriteLine("{0}: <- ConsumeMessage({1}), -> {2}", this.Name, messageHeader.Id, messageConsumed);
        ///     }
        /// 
        ///     void ILinkProbe<T>.OnReserveResponse(DataflowMessageHeader messageHeader, bool reserved)
        ///     {
        ///         Console.WriteLine("{0}: <- ReserveMessage({1}), -> {2}", this.Name, messageHeader.Id, reserved);
        ///     }
        /// 
        ///     void ILinkProbe<T>.OnUnlink()
        ///     {
        ///         Console.WriteLine("{0}: Unlink");
        ///     }
        /// }
        /// ]]></code>
        /// </example>
        public static IDisposable LinkWithProbe<T>(this ISourceBlock<T> source, ITargetBlock<T> target, ILinkProbe<T> probe)
        {
            return source.LinkWithProbe(target, s_defaultOptions, probe);
        }
    }
}
