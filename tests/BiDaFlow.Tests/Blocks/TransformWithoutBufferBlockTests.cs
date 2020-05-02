using System.Threading;
using System.Threading.Tasks;
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
        public async Task TestTransform()
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

            (await transformBlock.ReceiveAsync(TestUtils.SometimeSoon)).Is(10);
            inputBlock.Count.Is(2);
            transformBlock.Completion.IsCompleted.IsFalse();

            (await transformBlock.ReceiveAsync(TestUtils.SometimeSoon)).Is(20);
            inputBlock.Count.Is(1);
            transformBlock.Completion.IsCompleted.IsFalse();

            (await transformBlock.ReceiveAsync(TestUtils.SometimeSoon)).Is(30);
            inputBlock.Count.Is(0);

            await transformBlock.Completion.CompleteSoon();
        }

        [Fact]
        public async Task TestCancel()
        {
            var cts = new CancellationTokenSource();
            var transformBlock = new TransformWithoutBufferBlock<int, int>(x => x, cts.Token);

            transformBlock.Completion.IsCompleted.IsFalse();

            cts.Cancel();

            await transformBlock.Completion.CanceledSoon();
        }

        [Fact]
        public void TestLink()
        {
            var transformBlock = new TransformWithoutBufferBlock<int, int>(x => x);

            var target1 = new BufferBlock<int>(new DataflowBlockOptions() { BoundedCapacity = 1 });
            var target2 = new BufferBlock<int>(new DataflowBlockOptions() { BoundedCapacity = 1 });
            var target3 = new BufferBlock<int>(new DataflowBlockOptions() { BoundedCapacity = 1 });

            transformBlock.LinkTo(target1);
            transformBlock.LinkTo(target2, x => x != 2);
            transformBlock.LinkTo(target3);

            transformBlock.Post(1).IsTrue();
            transformBlock.Post(2).IsTrue();
            transformBlock.Post(3).IsTrue();

            target1.Receive(TestUtils.SometimeSoon).Is(1);
            target2.Receive(TestUtils.SometimeSoon).Is(3);
            target3.Receive(TestUtils.SometimeSoon).Is(2);
        }

        [Fact]
        public async Task TestMaxMessages()
        {
            var transformBlock = new TransformWithoutBufferBlock<int, int>(x => x);
            var targetBlock = new BufferBlock<int>();
            transformBlock.LinkTo(targetBlock, new DataflowLinkOptions() { MaxMessages = 1, PropagateCompletion = true });

            transformBlock.Post(1).IsTrue();

            (await targetBlock.ReceiveAsync(TestUtils.SometimeSoon)).Is(1);
            await targetBlock.Completion.NeverComplete();

            transformBlock.Post(2).IsFalse();
        }

        [Fact]
        public async Task TestCompleteAndCancelSending()
        {
            var testBlock = new TransformWithoutBufferBlock<int, int>(x => x);

            var sendTask = testBlock.SendAsync(1);
            await sendTask.NeverComplete();

            testBlock.Complete();

            (await sendTask.CompleteSoon()).IsFalse();
        }
    }
}
