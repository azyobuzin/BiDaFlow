using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Fluent
{
    public class EncapsulateTests
    {
        [Fact]
        public async Task Propagator_TestMaxMessages()
        {
            // Tests EncapsulateAsPropagatorBlock is working as a work around for https://github.com/dotnet/runtime/issues/35751

            // Set 1 to BoundedCapacity to postpone offers
            var tenTimesTransformer = new TransformBlock<int, int>(
                x => x * 10,
                new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 });
            var propagator = new BufferBlock<int>(new DataflowBlockOptions() { BoundedCapacity = 1 })
                .Chain(tenTimesTransformer);

            var outputs = new List<int>();

            var outputBlock = new ActionBlock<int>(
                async i =>
                {
                    outputs.Add(i);
                    await Task.Delay(100); // Delay to postpone offer
                },
                new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 });

            propagator.LinkTo(outputBlock);

            // sourceBlock emits 1, 2, 3, 4, 5
            var sourceBlock = new BufferBlock<int>();
            for (var i = 1; i <= 5; i++) sourceBlock.Post(i);

            sourceBlock.LinkTo(propagator, new DataflowLinkOptions() { MaxMessages = 2 });

            await Task.Delay(500);

            // Expect first 2 items
            outputs.Is(10, 20);
        }

        [Fact]
        public async Task Target_TestMaxMessages()
        {
            var outputs = new List<int>();

            var tenTimesTransformer = new TransformBlock<int, int>(
                x => x * 10,
                new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 });
            var target = tenTimesTransformer
                .ChainToTarget(new ActionBlock<int>(
                    async i =>
                    {
                        outputs.Add(i);
                        await Task.Delay(100); // Delay to postpone offer
                    },
                    new ExecutionDataflowBlockOptions() { BoundedCapacity = 1 }
                ));

            // sourceBlock emits 1, 2, 3, 4, 5
            var sourceBlock = new BufferBlock<int>();
            for (var i = 1; i <= 5; i++) sourceBlock.Post(i);

            sourceBlock.LinkTo(target, new DataflowLinkOptions() { MaxMessages = 2 });

            await Task.Delay(500);

            // Expect first 2 items
            outputs.Is(10, 20);
        }
    }
}
