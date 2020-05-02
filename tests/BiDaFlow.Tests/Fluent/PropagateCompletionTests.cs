using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Fluent
{
    public class PropagateCompletionTests
    {
        [Fact]
        public async Task TestPropagateComplete()
        {
            var sourceBlock = new BufferBlock<int>();
            var targetBlock = new BufferBlock<int>();
            sourceBlock.PropagateCompletion(targetBlock);

            targetBlock.Completion.IsCompleted.IsFalse();

            sourceBlock.Complete();

            await targetBlock.Completion.CompleteSoon();
        }

        [Fact]
        public async Task TestPropagateFault()
        {
            var sourceBlock = new BufferBlock<int>();
            var targetBlock = new BufferBlock<int>();
            sourceBlock.PropagateCompletion(targetBlock);

            targetBlock.Completion.IsCompleted.IsFalse();

            ((IDataflowBlock)sourceBlock).Fault(new Exception("test"));

            var aex = (await Assert
                .ThrowsAsync<AggregateException>(() => targetBlock.Completion.CompleteSoon()))
                .Flatten();
            aex.InnerExceptions.Count.Is(1);
            aex.InnerException!.Message.Is("test");
        }

        [Fact]
        public async Task TestUnlink()
        {
            var sourceBlock = new BufferBlock<int>();
            var targetBlock = new BufferBlock<int>();
            var unlinker = sourceBlock.PropagateCompletion(targetBlock);

            targetBlock.Completion.IsCompleted.IsFalse();

            unlinker.Dispose();
            sourceBlock.Complete();

            await targetBlock.Completion.NeverComplete();
            sourceBlock.Completion.IsCompleted.IsTrue();
        }
    }
}
