using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Fluent
{
    public class CompleteWhenTests
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
            var outputBlock = new BufferBlock<int>();

            sourceBlock.LinkWithCompletion(block1);
            sourceBlock.LinkWithCompletion(block2);
            block1.LinkTo(outputBlock);
            block2.LinkTo(outputBlock);
            outputBlock.CompleteWhen(block1.Completion, block2.Completion);

            sourceBlock.Post(1); // to block1
            sourceBlock.Post(2); // to block2
            sourceBlock.Post(3); // to block1
            sourceBlock.Post(4); // to block2
            sourceBlock.Complete();

            var timeout = new TimeSpan(100 * TimeSpan.TicksPerMillisecond);
            (await outputBlock.ReceiveAsync(timeout)).Is(1);
            (await outputBlock.ReceiveAsync(timeout)).Is(20);
            (await outputBlock.ReceiveAsync(timeout)).Is(3);
            (await outputBlock.ReceiveAsync(timeout)).Is(40);
            await outputBlock.Completion.CompleteSoon();
        }

        [Fact]
        public async Task TestFault()
        {
            var testBlock = new BufferBlock<int>();

            var task1 = Task.Delay(1000);
            var task2 = Task.Run(() => throw new Exception("Error"));

            // CompleteWhen does not wait for task1 because task2 will soon throw
            testBlock.CompleteWhen(task1, task2);

            var aex = await Assert.ThrowsAnyAsync<AggregateException>(() =>
                Task.WhenAny(testBlock.Completion, Task.Delay(TestUtils.SometimeSoon)).Unwrap());

            aex.InnerException!.Message.Is("Error");
        }
    }
}
