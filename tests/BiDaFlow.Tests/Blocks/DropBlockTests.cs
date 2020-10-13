using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Blocks;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Blocks
{
    public class DropBlockTests
    {
        [Fact]
        public async Task TestBasicUsage()
        {
            var sourceBlock = new BufferBlock<int>();
            for (int i = 1; i <= 5; i++)
                sourceBlock.Post(i);
            sourceBlock.Complete();

            // Drops even values
            sourceBlock.LinkTo(new DropBlock<int>(), x => x % 2 == 0);

            var outputs = new List<int>();
            await sourceBlock
                .RunWith(new ActionBlock<int>(x => outputs.Add(x)))
                .Completion;

            outputs.Is(1, 3, 5);
        }

        [Fact]
        public async Task TestCancel()
        {
            var cts = new CancellationTokenSource();
            var testBlock = new DropBlock<int>(cts.Token);

            testBlock.Completion.IsCompleted.IsFalse();

            cts.Cancel();

            await testBlock.Completion.CanceledSoon();

            (await testBlock.SendAsync(1)).IsFalse();
        }
    }
}
