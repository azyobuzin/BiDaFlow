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
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

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
                    target.Complete();
                }
                else
                {
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

        public static ISourceBlock<T> AsSourceBlock<T>(this IEnumerable<T> enumerable, CancellationToken cancellationToken = default)
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
                    using (var enumerator = enumerable.GetEnumerator())
                    {
                        if (enumerator != null)
                        {
                            while (!cancellationToken.IsCancellationRequested && enumerator.MoveNext())
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

        public static IObserver<T> AsObserverDroppingOverflowItems<T>(this ITargetBlock<T> target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return new DroppingObserver<T>(target);
        }

        public static IPropagatorBlock<TInput, TOutput> Chain<TInput, TSourceOutput, TOutput>(
            this IPropagatorBlock<TInput, TSourceOutput> sourceBlock,
            IPropagatorBlock<TSourceOutput, TOutput> followerBlock)
        {
            if (sourceBlock == null) throw new ArgumentNullException(nameof(sourceBlock));
            if (followerBlock == null) throw new ArgumentNullException(nameof(followerBlock));

            sourceBlock.LinkWithCompletion(followerBlock);
            return DataflowBlock.Encapsulate(sourceBlock, followerBlock);
        }

        public static ISourceBlock<T> CompletedSourceBlock<T>()
        {
            return CompletedSourceBlockHolder<T>.Instance;
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

            var resultBlock = new TransformWithoutBufferBlock<T, T>(x => x);

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
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.DenyChildAttach,
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

            var mergedSource = Merge(sources.Prepend(propagator));
            return DataflowBlock.Encapsulate(propagator, mergedSource);
        }

        public static IPropagatorBlock<TInput, TOutput> Merge<TInput, TOutput>(this IPropagatorBlock<TInput, TOutput> propagator, params ISourceBlock<TOutput>[] sources)
        {
            return propagator.Merge((IEnumerable<ISourceBlock<TOutput>>)sources);
        }
    }
}
