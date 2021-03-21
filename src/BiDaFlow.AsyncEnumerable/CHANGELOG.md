# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased
### Fixed
- `DataflowAsyncEnumerable.AsSourceBlock(IAsyncEnumerable<T>)` would not dispose the enumerator when the cancel is requested

## [0.2.1] - 2021-03-17
### Fixed
- Fix throwing `ArgumentException` when input encapsulated block to `RunThroughDataflowBlock` ([#3](https://github.com/azyobuzin/BiDaFlow/issues/3))

## [0.2.0] - 2020-10-15
### Added
- Provides deterministic binary on NuGet

## [0.2.0-alpha1] - 2020-10-13
### Changed
- Remove dependency to BiDaFlow core library

## [0.1.0] - 2020-05-03
### Added
- Functions that convert between `ISourceBlock` and `IAsyncEnumerable`
- `RunThroughDataflowBlock` method to a dataflow block into `IAsyncEnumerable` method chain

[0.2.1]: https://github.com/azyobuzin/BiDaFlow/releases/tag/asyncenum-v0.2.1
[0.2.0]: https://github.com/azyobuzin/BiDaFlow/releases/tag/asyncenum-v0.2.0
[0.2.0-alpha1]: https://github.com/azyobuzin/BiDaFlow/releases/tag/asyncenum-v0.2.0-alpha1
[0.1.0]: https://github.com/azyobuzin/BiDaFlow/releases/tag/asyncenum-v0.1.0
