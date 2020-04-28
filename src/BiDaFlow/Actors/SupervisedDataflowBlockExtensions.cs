using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Blocks;
using BiDaFlow.Internal;

namespace BiDaFlow.Actors
{
    public static class SupervisedDataflowBlockExtensions
    {
        private static readonly ConditionalWeakTable<object, Dictionary<Type, object>> s_sourceBlocks = new ConditionalWeakTable<object, Dictionary<Type, object>>();
        private static readonly ConditionalWeakTable<object, Dictionary<Type, object>> s_targetBlocks = new ConditionalWeakTable<object, Dictionary<Type, object>>();

        public static ISourceBlock<TOutput> AsSourceBlock<T, TOutput>(this SupervisedDataflowBlock<T> supervisedBlock)
            where T : ISourceBlock<TOutput>
        {
            var dic = s_sourceBlocks.GetOrCreateValue(supervisedBlock);

            IPropagatorBlock<TOutput, TOutput> helperBlock;
            ISourceBlock<TOutput> sourceBlock;

            lock (dic)
            {
                if (dic.TryGetValue(typeof(TOutput), out var obj))
                    return (ISourceBlock<TOutput>)obj;

                helperBlock = new TransformWithoutBufferBlock<TOutput, TOutput>(IdentityFunc<TOutput>.Instance, supervisedBlock.TaskScheduler, CancellationToken.None);
                sourceBlock = new SupervisedSourceBlock<TOutput>(supervisedBlock, helperBlock);
                dic.Add(typeof(TOutput), sourceBlock);
            }

            // Report excepton to supervisedBlock if helperBlock completes with exception (it is a bug).
            helperBlock.Completion.ContinueWith(
                (t, state) =>
                {
                    var exception = t.Exception;
                    if (exception != null)
                        ((IDataflowBlock)state).Fault(exception);
                },
                supervisedBlock,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                supervisedBlock.TaskScheduler
            );

            if (!supervisedBlock.Completion.IsCompleted)
            {
                supervisedBlock.CurrentBlockObservable
                    .Subscribe(opt =>
                    {
                        if (!opt.HasValue) return;
                        opt.Value.LinkTo(helperBlock);
                    });
            }

            return sourceBlock;
        }

        public static ITargetBlock<TInput> AsTargetBlock<T, TInput>(this SupervisedDataflowBlock<T> supervisedBlock)
            where T : ITargetBlock<TInput>
        {
            var dic = s_targetBlocks.GetOrCreateValue(supervisedBlock);

            IPropagatorBlock<TInput, TInput> helperBlock;
            ITargetBlock<TInput> targetBlock;

            lock (dic)
            {
                if (dic.TryGetValue(typeof(TInput), out var obj))
                    return (ITargetBlock<TInput>)obj;

                helperBlock = new TransformWithoutBufferBlock<TInput, TInput>(IdentityFunc<TInput>.Instance, supervisedBlock.TaskScheduler, CancellationToken.None);
                targetBlock = new SupervisedTargetBlock<TInput>(supervisedBlock, helperBlock);
                dic.Add(typeof(TInput), targetBlock);
            }

            // Report excepton to supervisedBlock if helperBlock completes with exception (it is a bug).
            helperBlock.Completion.ContinueWith(
                (t, state) =>
                {
                    var exception = t.Exception;
                    if (exception != null)
                        ((IDataflowBlock)state).Fault(exception);
                },
                supervisedBlock,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                supervisedBlock.TaskScheduler
            );

            if (supervisedBlock.Completion.IsCompleted)
            {
                helperBlock.Complete();
            }
            else
            {
                supervisedBlock.CurrentBlockObservable
                    .Subscribe(
                        opt =>
                        {
                            if (!opt.HasValue) return;
                            helperBlock.LinkTo(opt.Value);
                        },
                        null,
                        // Complete helperBlock to get OfferMessage to return DecliningPermanently
                        helperBlock.Complete
                    );
            }

            return targetBlock;
        }

        public static IPropagatorBlock<TInput, TOutput> AsPropagatorBlock<T, TInput, TOutput>(this SupervisedDataflowBlock<T> supervisedBlock)
            where T : ITargetBlock<TInput>, ISourceBlock<TOutput>
        {
            return DataflowBlock.Encapsulate(supervisedBlock.AsTargetBlock<T, TInput>(), supervisedBlock.AsSourceBlock<T, TOutput>());
        }

        public static bool Post<TActor>(this SupervisedDataflowBlock<TActor> supervisedActor, Func<TActor, Envelope> createMessage)
            where TActor : Actor
        {
            var actorOpt = supervisedActor.CurrentBlock;
            if (!actorOpt.HasValue) return false;

            var actor = actorOpt.Value;
            var envelope = createMessage(actor);

            if (!ReferenceEquals(envelope.Address, actor))
                throw new InvalidOperationException("The destination of envelope returned by createMessage is not the specified actor.");

            return createMessage(actor).Post();
        }

        public static Task<bool> SendAsync<TActor>(this SupervisedDataflowBlock<TActor> supervisedActor, Func<TActor, Envelope> createMessage, CancellationToken cancellationToken)
            where TActor : Actor
        {
            if (supervisedActor.Completion.IsCompleted)
                return Task.FromResult(false);

            var tcs = new TaskCompletionSource<Task<bool>>();

            var subscription = supervisedActor.CurrentBlockObservable
                .Where(x => x.HasValue)
                .ReceiveOnce((actorOpt, ex, completed) =>
                {
                    if (ex != null || completed)
                    {
                        tcs.TrySetResult(Task.FromResult(false));
                        return;
                    }

                    var actor = actorOpt.Value;
                    var envelope = createMessage(actor);

                    if (!ReferenceEquals(envelope.Address, actor))
                    {
                        tcs.TrySetException(new InvalidOperationException("The destination of envelope returned by createMessage is not the specified actor."));
                        return;
                    }

                    tcs.TrySetResult(envelope.SendAsync(cancellationToken));
                });

            cancellationToken.Register(() =>
            {
                subscription.Dispose();
                tcs.TrySetCanceled(cancellationToken);
            });

            return tcs.Task.Unwrap();
        }

        // TODO: Actor support
    }
}
