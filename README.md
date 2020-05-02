# BiDaFlow

Battery Included Dataflow Library

# Features

## Fluent API for TPL Dataflow

```csharp
await Enumerable.Range(1, 100).AsSourceBlock()
    // RunWith connects to a ITargetBlock and returns a single IDataflowBlock
    .RunWith(
        new BatchBlock<int>(5, new GroupingDataflowBlockOptions { BoundedCapacity = 5 })
            // Chain makes a new IPropagatorBlock linking the upstream and downstream blocks
            .Chain(new TransformBlock<int[], int>(
                x => x.Sum(),
                new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = 4,
                    MaxDegreeOfParallelism = 4,
                }))
            // ToTargetBlock makes a new ITargetBlock linking the upstream and downstream blocks
            .ToTargetBlock(new ActionBlock<int>(x => Console.WriteLine(x)))
    )
    .Completion;
```

## AsyncEnumerable Integration

```csharp
await AsyncEnumerable.Range(1, 100)
    // Process elements in parallel with IPropagatorBlock
    .RunThroughDataflowBlock(() =>
        new TransformBlock<int, int>(
            x => x * 10,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 6,
                MaxDegreeOfParallelism = 4,
                SingleProducerConstrained = true,
            })
    )
    // The result is an IAsyncEnumerable
    // Subsequent process can be written with System.Linq.Async
    .ForEachAsync(x => Console.WriteLine(x));
```

# Roadmap

- BiDaFlow.Actors - a lightweight actor-like programming framework with IDataflowBlock
- BiDaFlow.ReactiveStreams - integration with [Reactive Streams](https://github.com/reactive-streams/reactive-streams-dotnet), TPL Dataflow and AsyncEnumerable
