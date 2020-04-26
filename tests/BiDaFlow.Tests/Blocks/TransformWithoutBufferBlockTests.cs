using System.Threading;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Blocks;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Blocks
{
    public class TransformWithoutBufferBlockTests
    {
        [Fact]
        public void TestTransform()
        {
            var inputBlock = new BufferBlock<int>();
            var transformBlock = new TransformWithoutBufferBlock<int, int>(x => x * 10);
            inputBlock.LinkWithCompletion(transformBlock);

            inputBlock.Post(1);
            inputBlock.Post(2);
            inputBlock.Post(3);
            inputBlock.Complete();

            inputBlock.Count.Is(3);
            transformBlock.Completion.IsCompleted.IsFalse();

            transformBlock.Receive().Is(10);
            inputBlock.Count.Is(2);
            transformBlock.Completion.IsCompleted.IsFalse();

            transformBlock.Receive().Is(20);
            inputBlock.Count.Is(1);
            transformBlock.Completion.IsCompleted.IsFalse();

            transformBlock.Receive().Is(30);
            inputBlock.Count.Is(0);

            transformBlock.Completion.Wait(TestUtils.CancelSometimeSoon());
        }

        [Fact]
        public void TestCancel()
        {
            var cts = new CancellationTokenSource();
            var transformBlock = new TransformWithoutBufferBlock<int, int>(x => x, cts.Token);

            transformBlock.Completion.IsCompleted.IsFalse();

            cts.Cancel();

            transformBlock.Completion.Wait(TestUtils.CancelSometimeSoon());
        }
    }
}
