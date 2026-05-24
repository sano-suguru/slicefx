# Changelog

All notable changes to Slice will be documented in this file.

This project follows semantic versioning once stable packages are published. Before then, `0.x` and preview versions may change APIs while the framework is experimental.

## 0.1.0-preview.1 - Unreleased, not yet on NuGet

### Preview scope

- Initial experimental framework, source generator, adapters, WASI runtime, and CLI surface.
- Core runtime package ID is `Slice.Core` because `Slice` is already used on NuGet.
- `Slice.SourceGenerator` is required for generated `AddSlice` / `MapSlices` registrations in the `Slice` namespace.
- Feature assemblies emit module helpers, and hosts can aggregate referenced Slice feature modules.
- Package metadata is prepared, but the preview has not been pushed to NuGet yet.

### Added

- Added route metadata manifests with portability vocabulary for `portable`, `partial`, and `aspnet-only` routes.
- Added CLI scaffolding, route inspection, compatibility reporting, and C# typed client generation.
- Added AWS Lambda, TestHost, and WASI experimental adapters.
- Added DataAnnotations validation and `ISliceValidator<T>` custom validation support.
- Added OSS release documents: license, contribution guide, security policy, code of conduct, and changelog.
- Added GitHub Pages landing page under `docs/`.
- Added NuGet package metadata for the framework, source generator, adapters, and CLI.
- Added CI test and package verification gates for preview readiness.
- Documented the WASI host support matrix, unstable upstream toolchain boundary, and reproducible Cloudflare Workers dependency pins.

### Fixed before first publish

- Hardened handler discovery, duplicate endpoint detection, generated identifier sanitization, and WASI malformed JSON handling.
- Improved CLI handling for hyphenated project names, partial feature classes, generic handler parameters, nullable query parameters, and array query parameters.
