using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Blocks
{
    /// <summary>
    /// Example tests. These tests must be passed for all IDataflowBlock implementations.
    /// </summary>
    public class BufferBlockTests
    {
        [Fact]
        public async Task TestCancel()
        {
            var cts = new CancellationTokenSource();
            var testBlock = new BufferBlock<int>(new DataflowBlockOptions() { CancellationToken = cts.Token });

            testBlock.Completion.IsCompleted.IsFalse();

            cts.Cancel();

            await testBlock.Completion.CanceledSoon();
        }

        [Fact]
        public void TestLink()
        {
            var testBlock = new BufferBlock<int>();

            var target1 = new BufferBlock<int>(new DataflowBlockOptions() { BoundedCapacity = 1 });
            var target2 = new BufferBlock<int>(new DataflowBlockOptions() { BoundedCapacity = 1 });
            var target3 = new BufferBlock<int>(new DataflowBlockOptions() { BoundedCapacity = 1 });

            testBlock.LinkTo(target1);
            testBlock.LinkTo(target2, x => x != 2);
            testBlock.LinkTo(target3);

            testBlock.Post(1).IsTrue();
            testBlock.Post(2).IsTrue();
            testBlock.Post(3).IsTrue();

            target1.Receive(TestUtils.CancelSometimeSoon()).Is(1);
            target2.Receive(TestUtils.CancelSometimeSoon()).Is(3);
            target3.Receive(TestUtils.CancelSometimeSoon()).Is(2);
        }

        [Fact]
        public async Task TestMaxMessages()
        {
            var testBlock = new BufferBlock<int>(new DataflowBlockOptions() { BoundedCapacity = 1 });
            var targetBlock = new BufferBlock<int>();
            testBlock.LinkTo(targetBlock, new DataflowLinkOptions() { MaxMessages = 1, PropagateCompletion = true });

            testBlock.Post(1).IsTrue();

            targetBlock.Receive(TestUtils.CancelSometimeSoon()).Is(1);
            await targetBlock.Completion.NeverComplete();

            testBlock.Post(2).IsTrue();
            testBlock.Post(3).IsFalse("Reach BoundedCapacity");
        }

        [Fact]
        public async Task TestCompleteAndCancelSending()
        {
            var testBlock = new BufferBlock<int>(new DataflowBlockOptions() { BoundedCapacity = 1 });
            testBlock.Post(1).IsTrue();

            var sendTask = testBlock.SendAsync(2);
            await sendTask.NeverComplete();

            testBlock.Complete();

            (await sendTask.CompleteSoon()).IsFalse();
        }
    }
}
