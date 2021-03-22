using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Blocks;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Blocks
{
    [Obsolete("Tests for obsolete member")]
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

            (await transformBlock.SendAsync(1)).IsFalse();
        }

        [Fact]
        public void TestLink()
        {
            var transformBlock = new TransformWithoutBufferBlock<int, int>(x => x);

            var target1 = new WriteOnceBlock<int>(null);
            var target2 = new WriteOnceBlock<int>(null);
            var target3 = new WriteOnceBlock<int>(null);

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
        public async Task TestMultiProducerAndSlowConsumer()
        {
            var actionCount = 0;

            var producer1 = new BufferBlock<int>();
            var producer2 = new BufferBlock<int>();
            var transformBlock = new TransformWithoutBufferBlock<int, int>(x => x);
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

            producer1.LinkTo(transformBlock);
            producer2.LinkTo(transformBlock);
            transformBlock.LinkTo(consumer);

            for (var i = 1; i <= 3; i++)
            {
                producer1.Post(i);
                producer2.Post(i);
            }

            await Task.Delay(new TimeSpan(50 * 6 * TimeSpan.TicksPerMillisecond) + TestUtils.SometimeSoon);
            actionCount.Is(6);
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

        [Fact(Skip = "TransformWithoutBufferBlock can't avoid this isssue in principle.")]
        public async Task TestFastProducerMultiConsumerStressTest()
        {
            // https://github.com/azyobuzin/BiDaFlow/issues/4

            var producer = new BufferBlock<int>();
            var testBlock1 = new TransformWithoutBufferBlock<int, int>(x => x);
            var testBlock2 = new TransformWithoutBufferBlock<int, int>(x => x);
            var outputs = new List<int>();
            var slowConsumer = new ActionBlock<int>(outputs.Add, new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 });

            producer.LinkWithCompletion(testBlock1);
            producer.LinkWithCompletion(testBlock2);
            testBlock1.LinkTo(slowConsumer);
            testBlock2.LinkTo(slowConsumer);

            var values = Enumerable.Range(1, 10000);
            foreach (var i in values)
                producer.Post(i);
            producer.Complete();

            await Task.WhenAll(testBlock1.Completion, testBlock2.Completion)
                .CompleteWithin(new TimeSpan(5 * TimeSpan.TicksPerSecond));

            await Task.Delay(100);

            outputs.OrderBy(x => x).Is(values);
        }
    }
}
