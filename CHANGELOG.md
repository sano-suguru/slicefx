# Changelog

All notable changes to SliceFx will be documented in this file.

This project follows semantic versioning once stable packages are published. Before then, `0.x` and preview versions may change APIs while the framework is experimental.

## 0.1.0-preview.1 - Unreleased, not yet on NuGet

Initial experimental release of the SliceFx framework.

### Added

- Core framework: `[Feature]`, `[Filter<T>]`, `ISliceValidator<T>`, and `SliceValidationResult`.
- Source generator emitting `AddSlice` / `MapSlices` with DataAnnotations validation and `ISliceValidator<T>` support. Generated registration avoids per-request reflection (AOT-friendly).
- Route metadata manifest with portability vocabulary (`portable`, `partial`, `aspnet-only`) for tooling and deployment.
- Cross-assembly feature aggregation via `<SliceFxReferencedAssemblies>` or `<SliceFxAggregateReferences>`.
- AWS Lambda adapter (`SliceFx.Lambda`) and function-per-feature Lambda adapter (`SliceFx.Lambda.FunctionPerFeature`).
- In-process test host (`SliceFx.TestHost`) via `WebApplicationFactory`.
- WASI adapter (`SliceFx.Wasi`) targeting `wasi:http/incoming-handler@0.2.0` via componentize-dotnet.
- CLI tool (`slicefx`): `new feature`, `new filter`, `routes`, and `client csharp` commands.
- Typed client generation with `SliceApiException` / `SliceApiError` for structured validation error access.
