using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Blocks;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Blocks
{
    public class FilterBlockTests
    {
        [Fact]
        public async Task TestFilter()
        {
            var inputBlock = new BufferBlock<int>();
            // drops even values
            var testBlock = new FilterBlock<int>(x => x % 2 != 0, new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 });
            inputBlock.LinkWithCompletion(testBlock);

            for (var i = 1; i <= 6; i++)
                inputBlock.Post(i);
            inputBlock.Complete();

            await Task.Delay(TestUtils.SometimeSoon);

            inputBlock.Count.Is(5);
            testBlock.Completion.IsCompleted.IsFalse();

            (await testBlock.ReceiveAsync(TestUtils.SometimeSoon)).Is(1);
            await Task.Delay(TestUtils.SometimeSoon);
            inputBlock.Count.Is(3);
            testBlock.Completion.IsCompleted.IsFalse();

            (await testBlock.ReceiveAsync(TestUtils.SometimeSoon)).Is(3);
            await Task.Delay(TestUtils.SometimeSoon);
            inputBlock.Count.Is(1);
            testBlock.Completion.IsCompleted.IsFalse();

            (await testBlock.ReceiveAsync(TestUtils.SometimeSoon)).Is(5);
            await Task.Delay(TestUtils.SometimeSoon);
            inputBlock.Count.Is(0);

            await testBlock.Completion.CompleteSoon();
        }

        [Fact]
        public async Task TestCancel()
        {
            var cts = new CancellationTokenSource();
            var testBlock = new FilterBlock<int>(
                x => x % 2 != 0,
                new ExecutionDataflowBlockOptions() { CancellationToken = cts.Token });

            testBlock.Completion.IsCompleted.IsFalse();

            cts.Cancel();

            await testBlock.Completion.CanceledSoon();

            (await testBlock.SendAsync(1)).IsFalse();
        }

        [Fact]
        public async void TestLink()
        {
            var testBlock = new FilterBlock<int>(x => x % 2 != 0);

            var target1 = new WriteOnceBlock<int>(null);
            var target2 = new WriteOnceBlock<int>(null);
            var target3 = new WriteOnceBlock<int>(null);

            testBlock.LinkTo(target1);
            testBlock.LinkTo(target2, x => x != 3);
            testBlock.LinkTo(target3);

            testBlock.Post(1).IsTrue();
            (await testBlock.SendAsync(2).CompleteSoon()).IsTrue(); // drop
            (await testBlock.SendAsync(3).CompleteSoon()).IsTrue();
            (await testBlock.SendAsync(4).CompleteSoon()).IsTrue(); // drop
            (await testBlock.SendAsync(5).CompleteSoon()).IsTrue();

            target1.Receive(TestUtils.SometimeSoon).Is(1);
            target2.Receive(TestUtils.SometimeSoon).Is(5);
            target3.Receive(TestUtils.SometimeSoon).Is(3);
        }

        [Fact]
        public async Task TestMultiProducerAndSlowConsumer()
        {
            var actionCount = 0;

            var producer1 = new BufferBlock<int>();
            var producer2 = new BufferBlock<int>();
            var testBlock = new FilterBlock<int>(x => x % 2 != 0);
            var consumer = new ActionBlock<int>(
                async i =>
                {
                    await Task.Delay(50);
                    actionCount++;
                },
                new ExecutionDataflowBlockOptions()
                {
                    BoundedCapacity = 1,
                });

            producer1.LinkTo(testBlock);
            producer2.LinkTo(testBlock);
            testBlock.LinkTo(consumer);

            for (var i = 1; i <= 3; i++)
            {
                // 1 and 3 will pass the filter
                producer1.Post(i);
                producer2.Post(i);
            }

            await Task.Delay(new TimeSpan(50 * 4 * TimeSpan.TicksPerMillisecond) + TestUtils.SometimeSoon);
            actionCount.Is(4);
        }

        [Fact]
        public async Task TestMaxMessages()
        {
            var testBlock = new FilterBlock<int>(x => x % 2 != 0, new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 });
            var targetBlock = new BufferBlock<int>();
            testBlock.LinkTo(targetBlock, new DataflowLinkOptions() { MaxMessages = 1, PropagateCompletion = true });

            testBlock.Post(0).IsTrue();
            (await testBlock.SendAsync(1).CompleteSoon()).IsTrue();

            (await targetBlock.ReceiveAsync(TestUtils.SometimeSoon)).Is(1);
            await targetBlock.Completion.NeverComplete();

            (await testBlock.SendAsync(2).CompleteSoon()).IsTrue(); // drop
            (await testBlock.SendAsync(3).CompleteSoon()).IsTrue();
            await testBlock.SendAsync(4).NeverComplete(); // Reach BoundedCapacity"
        }

        [Fact]
        public async Task TestCompleteAndCancelSending()
        {
            var testBlock = new FilterBlock<int>(x => x % 2 != 0, new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 });
            testBlock.Post(1).IsTrue();

            var sendTask = testBlock.SendAsync(2);
            await sendTask.NeverComplete();

            testBlock.Complete();

            (await sendTask.CompleteSoon()).IsFalse();
        }

        [Fact]
        public async Task TestTryReceive()
        {
            var inputBlock = new BufferBlock<int>();
            var testBlock = new FilterBlock<int>(x => x % 2 != 0);
            inputBlock.LinkWithCompletion(testBlock);

            for (var i = 1; i <= 6; i++)
                inputBlock.Post(i);

            await Task.Delay(TestUtils.SometimeSoon);

            testBlock.TryReceive(out var item).IsTrue();
            item.Is(1);

            await Task.Delay(TestUtils.SometimeSoon);

            testBlock.TryReceive(out item).IsTrue();
            item.Is(3);

            await Task.Delay(TestUtils.SometimeSoon);

            testBlock.TryReceive(out item).IsTrue();
            item.Is(5);

            await Task.Delay(TestUtils.SometimeSoon);

            testBlock.TryReceive(out item).IsFalse();
        }

        [Fact]
        public async Task TestTryReceiveAll()
        {
            var inputBlock = new BufferBlock<int>();
            var testBlock = new FilterBlock<int>(x => x % 2 != 0);
            inputBlock.LinkWithCompletion(testBlock);

            for (var i = 1; i <= 6; i++)
                inputBlock.Post(i);

            await Task.Delay(TestUtils.SometimeSoon);

            testBlock.TryReceiveAll(out var items).IsTrue();
            items.Is(1, 3, 5);
        }
    }
}
