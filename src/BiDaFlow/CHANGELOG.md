# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2021-03-22
### Added
- Add simple encapulation methods
    - `FluentDataflow.EncapsulateAsTargetBlock`
    - `FluentDataflow.EncapsulateAsSourceBlock`
    - `FluentDataflow.EncapsulateAsPropagatorBlock`
    - `FluentDataflow.EncapsulateAsDataflowBlock`
- `DropBlock`
- `FluentDataflow.LinkWithProbe` to log the behavior of the block
- `FluentDataflow.CompleteWhen`
    - Use this instead of `Merge`.
- Provides deterministic binary on NuGet

### Deprecated
- Obsolete `TransformWithoutBufferBlock` because it can cause a deadlock and it seems that the issue cannot be fixed because of the protocol of DataflowBlock.
- Obsolete `FluentDataflow.Merge` because it uses `TransformWithoutBufferBlock`. Use `CompleteWhen` instead.

### Fixed
- `TransformWithoutBufferBlock` would not work with multiple producers and a slow consumer
- `FluentDataflow.AsSourceBlock(IEnumerable<T>)` would not dispose the enumerator when the cancel is requested

## [0.1.0] - 2020-05-03
### Added
- `FluentDataflow` class that provides functions to construct and manipulate dataflow blocks
- `TransformWithoutBufferBlock`

[0.2.0]: https://github.com/azyobuzin/BiDaFlow/releases/tag/core-v0.2.0
[0.1.0]: https://github.com/azyobuzin/BiDaFlow/releases/tag/core-v0.1.0
