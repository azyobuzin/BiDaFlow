using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Fluent
{
    public class MergeTests
    {
        [Fact]
        public async Task TestMerge()
        {
            IPropagatorBlock<int, int> CreateDelayBlock(int delay, int multiplier)
            {
                return new TransformBlock<int, int>(
                    async x =>
                    {
                        await Task.Delay(delay);
                        return x * multiplier;
                    },
                    new ExecutionDataflowBlockOptions()
                    {
                        BoundedCapacity = 1,
                        SingleProducerConstrained = true,
                    });
            }

            var sourceBlock = new BufferBlock<int>();
            var block1 = CreateDelayBlock(50, 1);
            var block2 = CreateDelayBlock(75, 10);

            sourceBlock.LinkWithCompletion(block1);
            sourceBlock.LinkWithCompletion(block2);

            var outputBlock = FluentDataflow.Merge(new ISourceBlock<int>[] { block1, block2 });

            sourceBlock.Post(1); // to block1
            sourceBlock.Post(2); // to block2
            sourceBlock.Post(3); // to block1
            sourceBlock.Post(4); // to block2
            sourceBlock.Complete();

            var timeout = new TimeSpan(100 * TimeSpan.TicksPerMillisecond);
            outputBlock.Receive(timeout).Is(1);
            outputBlock.Receive(timeout).Is(20);
            outputBlock.Receive(timeout).Is(3);
            outputBlock.Receive(timeout).Is(40);
            await outputBlock.Completion.CompleteSoon();
        }

        [Fact]
        public async Task TestSlowConsumer()
        {
            IPropagatorBlock<int, int> CreateDelayBlock(int delay, int multiplier)
            {
                return new TransformBlock<int, int>(
                    async x =>
                    {
                        await Task.Delay(delay);
                        return x * multiplier;
                    },
                    new ExecutionDataflowBlockOptions()
                    {
                        BoundedCapacity = 1,
                        SingleProducerConstrained = true,
                    });
            }

            var sourceBlock = new BufferBlock<int>();
            var block1 = CreateDelayBlock(50, 1);
            var block2 = CreateDelayBlock(75, 10);
            var testBlock = FluentDataflow.Merge(new ISourceBlock<int>[] { block1, block2 });
            var consumer = new TransformBlock<int, int>(
                async x => { await Task.Delay(50); return x; },
                new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 }
            );

            sourceBlock.LinkWithCompletion(block1);
            sourceBlock.LinkWithCompletion(block2);
            testBlock.LinkWithCompletion(consumer);

            sourceBlock.Post(1); // to block1
            sourceBlock.Post(2); // to block2
            sourceBlock.Post(3); // to block1
            sourceBlock.Post(4); // to block2
            sourceBlock.Complete();

            var timeoutToken = TestUtils.CancelAfter(new TimeSpan(300 * TimeSpan.TicksPerMillisecond));
            (await consumer.AsAsyncEnumerable().ToArrayAsync(timeoutToken))
                .Is(1, 20, 3, 40);
        }
    }
}
