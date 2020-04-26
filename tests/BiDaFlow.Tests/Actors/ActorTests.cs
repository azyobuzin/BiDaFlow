using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Actors;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Actors
{
    public partial class ActorTests
    {
        [Fact]
        public async Task TestOutput()
        {
            var actor = new TestOutputActor();
            var targetBlock = new BufferBlock<int>();
            actor.LinkWithCompletion(targetBlock);

            actor.TenTimes(1).Post().IsTrue();
            actor.TenTimes(2).Post().IsTrue();
            actor.Stop();

            var outputs = await targetBlock.ToAsyncEnumerable()
                .ToArrayAsync(TestUtils.CancelSometimeSoon());

            outputs.Is(10, 20);
        }

        [Fact]
        public async Task TestAsTargetBlock()
        {
            var actor = new TestOutputActor();
            var outputBlock = new BufferBlock<int>();
            actor.LinkWithCompletion(outputBlock);

            var actorTargetBlock = actor.AsTargetBlock<TestOutputActor, int>((a, n) => a.TenTimes(n));

            new[] { 1, 2 }.AsSourceBlock().LinkWithCompletion(actorTargetBlock);

            var outputs = await outputBlock.ToAsyncEnumerable()
                .ToArrayAsync(TestUtils.CancelSometimeSoon());

            outputs.Is(10, 20);
        }
    }
}
