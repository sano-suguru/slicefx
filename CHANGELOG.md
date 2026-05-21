# Changelog

All notable changes to Slice will be documented in this file.

This project follows semantic versioning once stable packages are published. Before then, `0.x` and preview versions may change APIs while the framework is experimental.

## Unreleased

### Changed (breaking)

- Removed the runtime reflection fallback (`SliceExtensions.AddSlice` / `MapSlices` in `Slice.Core`). `Slice.SourceGenerator` is now required. The generated methods are named `AddSlice` / `MapSlices` in namespace `Slice` (previously `AddSliceGenerated` / `MapSlicesGenerated` in `Slice.Generated`). Workers: `AddSliceGenerated(WorkerHostBuilder)` renamed to `AddSlice(WorkerHostBuilder)`.
- Added source-generated cross-assembly aggregation. Feature assemblies emit module helpers and hosts emit a single `AddSlice` / `MapSlices` surface that aggregates referenced Slice feature modules. Empty feature projects now emit no-op registrations and an empty route manifest.

### Added / Fixed

- Added CI test and package verification gates for preview readiness.
- Hardened handler discovery, duplicate endpoint detection, generated identifier sanitization, and Workers malformed JSON handling.
- Added route manifest portability vocabulary and improved CLI handling for hyphenated project names, partial feature classes, generic handler parameters, nullable query parameters, and array query parameters.

## 0.1.0-preview.1

### Added

- OSS release documents: license, contribution guide, security policy, code of conduct, and changelog.
- GitHub Pages landing page under `docs/`.
- NuGet package metadata for the framework, source generator, adapters, and CLI.
- Initial experimental framework, source generator, adapters, Workers runtime, and CLI surface.
