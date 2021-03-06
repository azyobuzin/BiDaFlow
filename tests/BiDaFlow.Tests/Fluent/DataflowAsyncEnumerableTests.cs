﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Fluent
{
    public class DataflowAsyncEnumerableTests
    {
        [Fact]
        public async Task TestRunThroughDataflowBlock()
        {
            var callCount = 0;
            IPropagatorBlock<int, int> PropagatorFactory()
            {
                callCount++;

                var multipler = callCount * 10;
                return new TransformBlock<int, int>(
                    x => x * multipler,
                    new ExecutionDataflowBlockOptions()
                    {
                        BoundedCapacity = 1,
                        SingleProducerConstrained = true,
                    }
                );
            }

            var enumerable = AsyncEnumerable.Range(1, 3)
                .RunThroughDataflowBlock(PropagatorFactory);

            // PropagatorFactory won't be called before GetAsyncEnumerator is called.
            callCount.Is(0);

            // enumerator1 multiplies an input by 10
            await using var enumerator1 = enumerable.GetAsyncEnumerator();
            callCount.Is(1);

            // enumerator2 multiplies an input by 20
            await using var enumerator2 = enumerable.GetAsyncEnumerator();
            callCount.Is(2);

            (await enumerator1.MoveNextAsync().AsTask().CompleteSoon()).Is(true);
            enumerator1.Current.Is(10);

            (await enumerator1.MoveNextAsync().AsTask().CompleteSoon()).Is(true);
            enumerator1.Current.Is(20);

            (await enumerator1.MoveNextAsync().AsTask().CompleteSoon()).Is(true);
            enumerator1.Current.Is(30);

            (await enumerator1.MoveNextAsync().AsTask().CompleteSoon()).Is(false);

            (await enumerator2.MoveNextAsync().AsTask().CompleteSoon()).Is(true);
            enumerator2.Current.Is(20);

            (await enumerator2.MoveNextAsync().AsTask().CompleteSoon()).Is(true);
            enumerator2.Current.Is(40);

            (await enumerator2.MoveNextAsync().AsTask().CompleteSoon()).Is(true);
            enumerator2.Current.Is(60);

            (await enumerator2.MoveNextAsync().AsTask().CompleteSoon()).Is(false);
        }

        [Fact]
        public async Task RunThroughDataflowBlock_Encapsulated()
        {
            // https://github.com/azyobuzin/BiDaFlow/issues/3

            IPropagatorBlock<int, int> PropagatorFactory()
            {
                var block = new BufferBlock<int>();
                return DataflowBlock.Encapsulate(block, block);
            }

            var results = await AsyncEnumerable.Range(1, 3)
                .RunThroughDataflowBlock(PropagatorFactory)
                .ToArrayAsync();

            results.Is(1, 2, 3);
        }

        [Fact]
        public async Task SourceBlockAsyncEnumerable_FastProducerMultiConsumerStressTest()
        {
            // https://github.com/azyobuzin/BiDaFlow/issues/4

            var producer = new BufferBlock<int>();
            var values = Enumerable.Range(1, 10000);

            async Task<IEnumerable<int>> ConsumeAsync()
            {
                return await producer.AsAsyncEnumerable().ToListAsync();
            }

            async Task GenerateValues()
            {
                await Task.Delay(50);
                foreach (var i in values)
                    producer.Post(i);
                producer.Complete();
            }

            var consumeTask1 = Task.Run(ConsumeAsync);
            var consumeTask2 = Task.Run(ConsumeAsync);
            var generateTask = Task.Run(GenerateValues);

            await Task.WhenAll(consumeTask1, consumeTask2, generateTask).CompleteWithin(new TimeSpan(5 * TimeSpan.TicksPerSecond));

            (await consumeTask1).Concat(await consumeTask2).OrderBy(x => x).Is(values);
        }
    }
}
