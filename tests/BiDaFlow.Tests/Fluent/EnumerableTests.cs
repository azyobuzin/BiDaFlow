using System.Linq;
using System.Threading.Tasks;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Fluent
{
    public class EnumerableTests
    {
        [Fact]
        public async Task EnumerableAsSourceBlock()
        {
            var values = await Enumerable.Range(1, 3)
                .AsSourceBlock()
                .ToAsyncEnumerable()
                .ToArrayAsync(TestUtils.CancelSometimeSoon());
            values.Is(1, 2, 3);
        }

        [Fact]
        public async Task AsyncEnumerableAsSourceBlock()
        {
            var values = await AsyncEnumerable.Range(1, 3)
                .AsSourceBlock()
                .ToAsyncEnumerable()
                .ToArrayAsync(TestUtils.CancelSometimeSoon());
            values.Is(1, 2, 3);
        }
    }
}
