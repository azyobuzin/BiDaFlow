# BiDaFlow

Battery Included Dataflow Library

[API Documentation](https://azyobuzin.github.io/BiDaFlow/api/index.html)

# Packages

| Package | Version | Changelog |
| ------- | ------- | --------- |
| [BiDaFlow](https://www.nuget.org/packages/BiDaFlow) | 0.1.0 | [CHANGELOG.md](BiDaFlow/CHANGELOG.md) |
| [BiDaFlow.AsyncEnumerable](https://www.nuget.org/packages/BiDaFlow.AsyncEnumerable) | 0.1.0 | [CHANGELOG.md](BiDaFlow.AsyncEnumerable/CHANGELOG.md) |

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

- BiDaFlow.ReactiveStreams - integration with [Reactive Streams](https://github.com/reactive-streams/reactive-streams-dotnet), TPL Dataflow and AsyncEnumerable
