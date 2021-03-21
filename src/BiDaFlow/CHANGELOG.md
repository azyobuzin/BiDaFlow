# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased
### Added
- `FluentDataflow.LinkWithProbe` to log the behavior of the block

### Fixed
- `TransformWithoutBufferBlock` would not work with multiple producers and a slow consumer
- `FluentDataflow.AsSourceBlock(IEnumerable<T>)` would not dispose the enumerator when the cancel is requested

## [0.2.0-alpha2] - 2020-10-15
### Added
- `DropBlock` and `FilterBlock`
    - Known issue: Tests fail at random?
- Provides deterministic binary on NuGet

### Fixed
- Fix wrong null handling in the classes encapsulating `ITargetBlock`

## [0.2.0-alpha1] - 2020-10-13
### Added
- Add simple encapulation methods
    - `FluentDataflow.EncapsulateAsTargetBlock`
    - `FluentDataflow.EncapsulateAsSourceBlock`
    - `FluentDataflow.EncapsulateAsPropagatorBlock`
    - `FluentDataflow.EncapsulateAsDataflowBlock`

### Changed
- Rename `FluentDataflow.ToTargetBlock` to `ChainToTarget`
    - `ToTargetBlock` had the same [issue as `DataflowBlock.Encapsulate`](https://github.com/dotnet/runtime/issues/35751). The new `ChainToTarget` is fixed.

## [0.1.0] - 2020-05-03
### Added
- `FluentDataflow` class that provides functions to construct and manipulate dataflow blocks
- `TransformWithoutBufferBlock`

[0.2.0-alpha2]: https://github.com/azyobuzin/BiDaFlow/releases/tag/core-v0.2.0-alpha2
[0.2.0-alpha1]: https://github.com/azyobuzin/BiDaFlow/releases/tag/core-v0.2.0-alpha1
[0.1.0]: https://github.com/azyobuzin/BiDaFlow/releases/tag/core-v0.1.0
