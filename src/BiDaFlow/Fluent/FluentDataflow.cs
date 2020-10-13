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
        private static readonly DataflowLinkOptions s_propagateCompletionOptions = new DataflowLinkOptions() { PropagateCompletion = true };

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
        /// Returns a <see cref="ISourceBlock{TOutput}"/> that have been completed.
        /// </summary>
        public static ISourceBlock<TOutput> CompletedSourceBlock<TOutput>()
        {
            return CompletedSourceBlockHolder<TOutput>.Instance;
        }

        public static ISourceBlock<TOutput> AsSourceBlock<TOutput>(this IEnumerable<TOutput> enumerable, CancellationToken cancellationToken = default)
        {
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));

            return new EnumerableSourceBlock<TOutput>(enumerable, null, cancellationToken);
        }

        public static ISourceBlock<TOutput> AsSourceBlock<TOutput>(this IEnumerable<TOutput> enumerable, TaskScheduler taskScheduler, CancellationToken cancellationToken)
        {
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));
            if (taskScheduler == null) throw new ArgumentNullException(nameof(taskScheduler));

            return new EnumerableSourceBlock<TOutput>(enumerable, taskScheduler, cancellationToken);
        }

        public static ISourceBlock<TOutput> ToSourceBlock<TOutput>(this IEnumerator<TOutput> enumerator, CancellationToken cancellationToken = default)
        {
            if (enumerator == null) throw new ArgumentNullException(nameof(enumerator));

            return new EnumerableSourceBlock<TOutput>(enumerator, null, cancellationToken);
        }

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

        public static ITargetBlock<TInput> EncapsulateAsTargetBlock<TInput>(ITargetBlock<TInput> entrance, IDataflowBlock terminal)
        {
            if (entrance == null) throw new ArgumentNullException(nameof(entrance));
            if (terminal == null) throw new ArgumentNullException(nameof(terminal));

            return new EncapsulatingTargetBlock<TInput>(entrance, terminal);
        }

        public static ISourceBlock<TOutput> EncapsulateAsSourceBlock<TOutput>(IDataflowBlock entrance, ISourceBlock<TOutput> terminal)
        {
            if (entrance == null) throw new ArgumentNullException(nameof(entrance));
            if (terminal == null) throw new ArgumentNullException(nameof(terminal));

            return new EncapsulatingSourceBlock<TOutput>(entrance, terminal);
        }

        public static IPropagatorBlock<TInput, TOutput> EncapsulateAsPropagatorBlock<TInput, TOutput>(ITargetBlock<TInput> entrance, ISourceBlock<TOutput> terminal)
        {
            if (entrance == null) throw new ArgumentNullException(nameof(entrance));
            if (terminal == null) throw new ArgumentNullException(nameof(terminal));

            // TODO: implement work around for https://github.com/dotnet/runtime/issues/35751
            return DataflowBlock.Encapsulate(entrance, terminal);
        }

        public static IDataflowBlock EncapsulateAsDataflowBlock(IDataflowBlock entrance, IDataflowBlock terminal)
        {
            if (entrance == null) throw new ArgumentNullException(nameof(entrance));
            if (terminal == null) throw new ArgumentNullException(nameof(terminal));

            return new EncapsulatingDataflowBlock(entrance, terminal);
        }

        public static IPropagatorBlock<TInput, TOutput> Chain<TInput, TSourceOutput, TOutput>(
            this IPropagatorBlock<TInput, TSourceOutput> source,
            IPropagatorBlock<TSourceOutput, TOutput> follower)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (follower == null) throw new ArgumentNullException(nameof(follower));

            source.LinkTo(follower, s_propagateCompletionOptions);
            return EncapsulateAsPropagatorBlock(source, follower);
        }

        public static ITargetBlock<TInput> ChainToTarget<TInput, TOutput>(this IPropagatorBlock<TInput, TOutput> source, ITargetBlock<TOutput> target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            source.LinkTo(target, s_propagateCompletionOptions);
            return EncapsulateAsTargetBlock(source, target);
        }

        public static IDataflowBlock RunWith<T>(this ISourceBlock<T> source, ITargetBlock<T> target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            source.LinkTo(target, s_propagateCompletionOptions);
            return EncapsulateAsDataflowBlock(source, target);
        }

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

        public static ISourceBlock<TOutput> Merge<TOutput>(params ISourceBlock<TOutput>[] sources)
        {
            return Merge((IEnumerable<ISourceBlock<TOutput>>)sources);
        }

        public static IPropagatorBlock<TInput, TOutput> Merge<TInput, TOutput>(this IPropagatorBlock<TInput, TOutput> propagator, IEnumerable<ISourceBlock<TOutput>> sources)
        {
            if (propagator == null) throw new ArgumentNullException(nameof(propagator));
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            var mergedSource = Merge(new[] { propagator }.Concat(sources));
            return EncapsulateAsPropagatorBlock(propagator, mergedSource);
        }

        public static IPropagatorBlock<TInput, TOutput> Merge<TInput, TOutput>(this IPropagatorBlock<TInput, TOutput> propagator, params ISourceBlock<TOutput>[] sources)
        {
            return propagator.Merge((IEnumerable<ISourceBlock<TOutput>>)sources);
        }
    }
}
