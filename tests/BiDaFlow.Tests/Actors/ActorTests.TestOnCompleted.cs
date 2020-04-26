using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Actors;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Actors
{
    partial class ActorTests
    {
        [Fact]
        public async Task TestOnCompleted()
        {
            var actor = new OutputOnCompletedActor();
            var targetBlock = new BufferBlock<int>();
            actor.LinkWithCompletion(targetBlock);

            // No item can be received before stop.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => targetBlock.ReceiveAsync(TestUtils.CancelSometimeSoon()));

            actor.Stop();

            var outputs = await targetBlock.ToAsyncEnumerable().ToArrayAsync(TestUtils.CancelSometimeSoon());
            outputs.Is(42);
        }

        [Fact]
        public void TestThrowOnCompleted()
        {
            var actor = new TestThrowOnCompletedActor();
            ((IDataflowBlock)actor).Fault(new Exception("test"));

            var aex = Assert
                .Throws<AggregateException>(() => actor.Completion.Wait(TestUtils.CancelSometimeSoon()))
                .Flatten();

            // Only exception thrown by OnCompleted can be catched here.
            aex.InnerExceptions.Count.Is(1);
            aex.InnerException!.Message.Is("thrown by OnCompleted");
        }

        private class OutputOnCompletedActor : Actor<int>
        {
            public void Stop()
            {
                this.Complete();
            }

            protected override async ValueTask OnCompleted(AggregateException? exception)
            {
                await this.SendOutputAsync(42);
            }
        }

        private class TestThrowOnCompletedActor : Actor
        {
            protected override ValueTask OnCompleted(AggregateException? exception)
            {
                throw new Exception("thrown by OnCompleted");
            }
        }
    }
}
