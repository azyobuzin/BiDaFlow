using System;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Fluent
{
    public class PropagateCompletionTests
    {
        [Fact]
        public void TestPropagateComplete()
        {
            var sourceBlock = new BufferBlock<int>();
            var targetBlock = new BufferBlock<int>();
            sourceBlock.PropagateCompletion(targetBlock);

            targetBlock.Completion.IsCompleted.IsFalse();

            sourceBlock.Complete();

            targetBlock.Completion.Wait(100).IsTrue();
        }

        [Fact]
        public void TestPropagateFault()
        {
            var sourceBlock = new BufferBlock<int>();
            var targetBlock = new BufferBlock<int>();
            sourceBlock.PropagateCompletion(targetBlock);

            targetBlock.Completion.IsCompleted.IsFalse();

            ((IDataflowBlock)sourceBlock).Fault(new Exception("test"));

            var ex = Assert.Throws<AggregateException>(() => targetBlock.Completion.Wait(100)).Flatten();
            ex.InnerExceptions.Count.Is(1);
            ex.InnerException.Message.Is("test");
        }

        [Fact]
        public void TestUnlink()
        {
            var sourceBlock = new BufferBlock<int>();
            var targetBlock = new BufferBlock<int>();
            var unlinker = sourceBlock.PropagateCompletion(targetBlock);

            targetBlock.Completion.IsCompleted.IsFalse();

            unlinker.Dispose();
            sourceBlock.Complete();

            targetBlock.Completion.Wait(100).IsFalse();
            sourceBlock.Completion.IsCompleted.IsTrue();
        }
    }
}
