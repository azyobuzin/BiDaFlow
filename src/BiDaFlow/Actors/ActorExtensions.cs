using System;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Blocks;
using BiDaFlow.Fluent;

namespace BiDaFlow.Actors
{
    public static class ActorExtensions
    {
        public static ITargetBlock<TInput> AsTargetBlock<TActor, TInput>(this TActor actor, Func<TActor, TInput, Envelope?> createMessage)
            where TActor : Actor
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            if (createMessage == null) throw new ArgumentNullException(nameof(createMessage));

            var iactor = (IActor)actor;

            var transformBlock = new TransformWithoutBufferBlock<TInput, Envelope?>(
                x => createMessage(actor, x),
                iactor.Engine.TaskScheduler,
                iactor.Engine.CancellationToken);
            transformBlock.LinkWithCompletion(iactor.Engine.Target);

            return transformBlock;
        }
    }
}
