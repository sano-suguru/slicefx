# Changelog

All notable changes to SliceFx will be documented in this file.

This project follows semantic versioning once stable packages are published. Before then, `0.x` and preview versions may change APIs while the framework is experimental.

## 0.1.0-preview.1 - Unreleased, not yet on NuGet

### Preview scope

- Initial experimental framework, source generator, adapters, WASI runtime, and CLI surface.
- Core runtime package ID is `SliceFx.Core` because `Slice` is already used on NuGet and `SliceFx` has lower search noise.
- `SliceFx.SourceGenerator` is required for generated `AddSlice` / `MapSlices` registrations in the `SliceFx` namespace.
- Feature assemblies emit module helpers, and hosts can explicitly aggregate referenced Slice feature modules.
- Package metadata is prepared, but the preview has not been pushed to NuGet yet.

### Added

- Added explicit opt-in for cross-assembly feature aggregation. Hosts map local features by default; use `<SliceFxReferencedAssemblies>FeatureLib;SharedSlices</SliceFxReferencedAssemblies>` for the preferred allow-list or `<SliceFxAggregateReferences>true</SliceFxAggregateReferences>` to preserve all-references aggregation.
- Added diagnostics for unconfigured referenced Slice feature modules and invalid `SliceFxAggregateReferences` values to prevent accidental HTTP surface expansion.
- Added route source assembly reporting to CLI route metadata and notices when generated tooling consumes aggregated referenced routes.
- Added route metadata manifests with portability vocabulary for `portable`, `partial`, and `aspnet-only` routes.
- Added CLI scaffolding, route inspection, compatibility reporting, and C# typed client generation.
- Added AWS Lambda, TestHost, and WASI experimental adapters.
- Added DataAnnotations validation and `ISliceValidator<T>` custom validation support.
- Added function-per-feature Lambda sample (`samples/SliceFx.LambdaFunctionPerFeatureSample/`) and restructured `docs/lambda.md` with a mode-selection guide, pipeline diagram, per-feature isolation section, and diagnostics reference table.
- Added OSS release documents: license, contribution guide, security policy, code of conduct, and changelog.
- Added GitHub Pages landing page under `docs/`.
- Added NuGet package metadata for the framework, source generator, adapters, and CLI.
- Added CI test and package verification gates for preview readiness.
- Documented the WASI host support matrix, single-component deployment boundary, lack of per-feature packaging, unstable upstream toolchain boundary, and reproducible Cloudflare Workers dependency pins.

### Fixed before first publish

- Hardened handler discovery, duplicate endpoint detection, generated identifier sanitization, and WASI malformed JSON handling.
- Fixed generated `RegularExpression` validation to match DataAnnotations full-value matching semantics across ASP.NET, WASI, and Lambda function-per-feature paths.
- Improved CLI handling for hyphenated project names, partial feature classes, generic handler parameters, nullable query parameters, and array query parameters.
