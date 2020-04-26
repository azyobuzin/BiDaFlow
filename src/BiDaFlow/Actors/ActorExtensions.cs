using System;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Blocks;
using BiDaFlow.Fluent;

namespace BiDaFlow.Actors
{
    public static class ActorExtensions
    {
        public static ITargetBlock<TInput> AsTargetBlock<TActor, TInput>(this TActor actor, Func<TActor, TInput, Envelope?> createMessage)
            where TActor : IActor
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            if (createMessage == null) throw new ArgumentNullException(nameof(createMessage));

            var transformBlock = new TransformWithoutBufferBlock<TInput, Envelope?>(
                x => createMessage(actor, x),
                actor.Engine.TaskScheduler,
                actor.Engine.CancellationToken);
            transformBlock.LinkWithCompletion(actor.Engine.Target);

            return transformBlock;
        }
    }
}
