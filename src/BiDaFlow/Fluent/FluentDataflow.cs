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
        public static IDisposable PropagateCompletion(this IDataflowBlock source, IDataflowBlock target)
        {
            return source.PropagateCompletion(target, WhenPropagate.Both);
        }

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

            task.ContinueWith(
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

        private static readonly DataflowLinkOptions s_propagateCompletionOptions = new DataflowLinkOptions() { PropagateCompletion = true };

        public static IDisposable LinkWithCompletion<TOutput>(this ISourceBlock<TOutput> source, ITargetBlock<TOutput> target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            return source.LinkTo(target, s_propagateCompletionOptions);
        }

        public static ISourceBlock<T> CompletedSourceBlock<T>()
        {
            return CompletedSourceBlockHolder<T>.Instance;
        }

        public static ISourceBlock<T> AsSourceBlock<T>(this IEnumerable<T> enumerable, CancellationToken cancellationToken = default)
        {
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));

            return new EnumerableSourceBlock<T>(enumerable, null, cancellationToken);
        }

        public static ISourceBlock<T> AsSourceBlock<T>(this IEnumerable<T> enumerable, TaskScheduler taskScheduler, CancellationToken cancellationToken)
        {
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));
            if (taskScheduler == null) throw new ArgumentNullException(nameof(taskScheduler));

            return new EnumerableSourceBlock<T>(enumerable, taskScheduler, cancellationToken);
        }

        public static ISourceBlock<T> ToSourceBlock<T>(this IEnumerator<T> enumerator, CancellationToken cancellationToken = default)
        {
            if (enumerator == null) throw new ArgumentNullException(nameof(enumerator));

            return new EnumerableSourceBlock<T>(enumerator, null, cancellationToken);
        }

        public static ISourceBlock<T> ToSourceBlock<T>(this IEnumerator<T> enumerator, TaskScheduler taskScheduler, CancellationToken cancellationToken)
        {
            if (enumerator == null) throw new ArgumentNullException(nameof(enumerator));
            if (taskScheduler == null) throw new ArgumentNullException(nameof(taskScheduler));

            return new EnumerableSourceBlock<T>(enumerator, taskScheduler, cancellationToken);
        }

        public static IObserver<T> AsObserverDroppingOverflowItems<T>(this ITargetBlock<T> target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return new DroppingObserver<T>(target);
        }

        public static IPropagatorBlock<TInput, TOutput> Chain<TInput, TSourceOutput, TOutput>(
            this IPropagatorBlock<TInput, TSourceOutput> source,
            IPropagatorBlock<TSourceOutput, TOutput> follower)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (follower == null) throw new ArgumentNullException(nameof(follower));

            source.LinkWithCompletion(follower);
            return DataflowBlock.Encapsulate(source, follower);
        }

        public static ITargetBlock<TInput> ToTargetBlock<TInput, TOutput>(this IPropagatorBlock<TInput, TOutput> source, ITargetBlock<TOutput> target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            source.LinkWithCompletion(target);
            return source;
        }

        public static IDataflowBlock RunWith<T>(this ISourceBlock<T> source, ITargetBlock<T> target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            source.LinkWithCompletion(target);
            return new RunWithBlock(source, target);
        }

        public static ISourceBlock<T> Merge<T>(IEnumerable<ISourceBlock<T>> sources)
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
            if (workingCount == 0) return CompletedSourceBlock<T>();

            var resultBlock = new TransformWithoutBufferBlock<T, T>(IdentityFunc<T>.Instance);

            foreach (var source in sourceList)
            {
                source.LinkTo(resultBlock);

                source.Completion.ContinueWith(
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

        public static ISourceBlock<T> Merge<T>(params ISourceBlock<T>[] sources)
        {
            return Merge((IEnumerable<ISourceBlock<T>>)sources);
        }

        public static IPropagatorBlock<TInput, TOutput> Merge<TInput, TOutput>(this IPropagatorBlock<TInput, TOutput> propagator, IEnumerable<ISourceBlock<TOutput>> sources)
        {
            if (propagator == null) throw new ArgumentNullException(nameof(propagator));
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            var mergedSource = Merge(new[] { propagator }.Concat(sources));
            return DataflowBlock.Encapsulate(propagator, mergedSource);
        }

        public static IPropagatorBlock<TInput, TOutput> Merge<TInput, TOutput>(this IPropagatorBlock<TInput, TOutput> propagator, params ISourceBlock<TOutput>[] sources)
        {
            return propagator.Merge((IEnumerable<ISourceBlock<TOutput>>)sources);
        }
    }
}
