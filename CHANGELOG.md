# Changelog

All notable changes to SliceFx will be documented in this file.

This project follows semantic versioning once stable packages are published. Before then, `0.x` and preview versions may change APIs while the framework is experimental.

## 0.1.0-preview.5 - 2026-05-30

### Added

- `ISpinVariables` and `InMemorySpinVariables` in `SliceFx.Wasi.Spin` — abstraction over `fermyon:spin/variables@2.0.0` with async `ValueTask<string?> GetAsync` surface and fail-closed semantics (undefined/provider error → `null`).
- `AddSpinVariables` registration overloads (instance + generic) in `WasiHostBuilderSpinExtensions`.
- `SliceFx.Wasi.Spin/README.md` — cron trigger wiring, cron expression format, `async func` limitation, `ISpinVariables` implementation guide, and source map.
- `docs/patterns/platform-abstraction.md` — WASI implementation notes: WIT-generated `*Interop` free-function binding pattern, async-surface-over-sync-WIT convention, `System.Security.Cryptography` unavailability with constant-time comparison workaround.

### Removed

- `SpinCronContext.Metadata` — the `spin:cron@3.0.0` WIT exposes only `{ timestamp: u64 }` with no metadata source. This is a breaking change: remove the second constructor argument from any `new SpinCronContext(fireTime, metadata)` call sites.

## 0.1.0-preview.4 - 2026-05-29

_Version-bumped; not separately tagged or published to NuGet._

### Added

- `SliceFx.Wasi.Spin` satellite — `ISpinCronHandler`, `SpinCronContext`, `SpinCronDispatcher`, `RecordingSpinCronHandler`, and `WasiHostBuilderSpinExtensions.AddSpinCronHandler` for handling Fermyon Spin cron triggers.
- WASI AOT fix: generated route dispatch now emits null-forgiving operators for non-nullable route parameters, preventing nullable-analysis warnings in NativeAOT builds.

## 0.1.0-preview.3 - 2026-05-29

_Version-bumped; not separately tagged or published to NuGet._

### Added

- `SliceFx.Wasi.HttpClient` satellite — `IWasiHttpClient` and `InMemoryWasiHttpClient` for abstracting outgoing HTTP over `wasi:http/outgoing-handler@0.2.0` in WASI builds.

## 0.1.0-preview.2 - 2026-05-29

_Version-bumped; not separately tagged or published to NuGet._

### Fixed

- Generated WASI route dispatch: added null-forgiving operator (`!`) for non-nullable route parameters. Prevents false nullable-annotation warnings from NativeAOT/ILC when the route regex guarantees a non-null capture.

## 0.1.0-preview.1 - 2026-05-29

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
