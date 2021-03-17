# BiDaFlow

**Get the power of parallel processing with TPL Dataflow**

[API Documentation](https://azyobuzin.github.io/BiDaFlow/api/index.html)

# Packages

| Package | Version | Changelog |
| ------- | ------- | --------- |
| [BiDaFlow](https://www.nuget.org/packages/BiDaFlow) | 0.1.0, 0.2.0-alpha2 | [CHANGELOG.md](src/BiDaFlow/CHANGELOG.md) |
| [BiDaFlow.AsyncEnumerable](https://www.nuget.org/packages/BiDaFlow.AsyncEnumerable) | 0.2.1 | [CHANGELOG.md](src/BiDaFlow.AsyncEnumerable/CHANGELOG.md) |

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
                    SingleProducerConstrained = true,
                }))
            // ChainToTarget makes a new ITargetBlock linking the upstream and downstream blocks
            .ChainToTarget(new ActionBlock<int>(x => Console.WriteLine(x)))
    )
    .Completion;
```

Do you want to make more customized block? You can use `FluentDataflow.EncapsulateAsDataflowBlock`.
See the API documentation and get the power to make blocks freely.

## AsyncEnumerable Integration

`IAsyncEnumerable` is the key interface of data flow with back pressure in .NET.
BiDaFlow.AsyncEnumerable empowers `IAsyncEnumerable` to be able to process data parallelly in manner of Task Async.

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
                EnsureOrdered = false,
                SingleProducerConstrained = true,
            })
    )
    // The result is an IAsyncEnumerable
    // Subsequent process can be written with System.Linq.Async
    .ForEachAsync(x => Console.WriteLine(x));
```

# Roadmap

- BiDaFlow.ReactiveStreams - integration with [Reactive Streams](https://github.com/reactive-streams/reactive-streams-dotnet), TPL Dataflow and AsyncEnumerable
