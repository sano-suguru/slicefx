# Changelog

All notable changes to SliceFx will be documented in this file.

This project follows semantic versioning once stable packages are published. Before then, `0.x` and preview versions may change APIs while the framework is experimental.

## 0.1.0-preview.13 - 2026-06-13

### Added

- **ASP.NET NativeAOT-safe dispatch** (`[assembly: SliceAspNetAot]`). Adding this assembly attribute
  switches the source generator from `RequestDelegateFactory`-based registration (reflection-bound)
  to generated `RequestDelegate` handlers that bind parameters, validate, and serialize responses
  exclusively via `System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>`. Publishing with
  `<PublishAot>true</PublishAot>` and `TreatWarningsAsErrors=true` now passes with zero
  IL2026/IL3050 diagnostics.
- `SliceJsonTarget.AspNet = 3` — mark a `JsonSerializerContext` with
  `[SliceJsonContext(SliceJsonTarget.AspNet)]` to supply JSON roots for the AOT path.
- `SliceFx.Core`: `SliceAspNetAotAttribute`, `SliceAotResults` (RFC 7807 Problem Details writer),
  `SliceAotArgumentBinder` (route/query/header binding over `HttpContext`),
  `SliceResultHttpResponseExtensions` (`SliceResult<T>`/`SliceResult` → `HttpContext.Response`),
  `SliceAotFilterContextBuilder` (`HttpContext` → `SliceFilterContext` for `[SliceFilter<T>]` chains).
- New diagnostics SLICE070–074 (parameter binding, missing JSON context, reflection-bound
  validation, `IResult` return, mixed-mode aggregation).
- `samples/SliceFx.AotSample` — NativeAOT sample on port 5103 with multi-stage `Dockerfile`
  (sdk:10.0 → runtime-deps:10.0-noble-chiseled, ~9 MB binary).
- `docs/aot.md` / `docs/ja/aot.md` — NativeAOT deployment guide.
- CI: `nativeaot-sample` parallel job (linux-x64 publish + smoke tests + binary size report).

## 0.1.0-preview.8 - 2026-06-01

### Changed

- `SliceFx.Wasi.SliceResult` (static factory class) renamed to `SliceFx.Wasi.WasiResults`,
  resolving a CS0104 name clash with the `SliceFx.SliceResult` Core struct added in preview.7.
  Factory calls become `WasiResults.Ok(...)`, `WasiResults.Problem(...)`, etc. Feature handles
  returning the `SliceResult<T>` / `SliceResult` Core structs are unaffected.

## 0.1.0-preview.7 - 2026-06-01

### Added

- `SliceResult<T>` (generic readonly struct, `SliceFx.Core`) — host-neutral typed result combining
  a typed response body with a variable HTTP status code. WASI `Handle` methods can now return
  `Task<SliceResult<TResponse>>` to express a 200 typed body **or** a 404/401/400 problem path from
  the same method. The source generator detects this return type, wires JSON dispatch automatically
  via `__JsonTypeInfo<T>()`, and the CLI client generator unwraps it to `Task<TResponse>`. (Fixes #5)
- `SliceResult` (non-generic readonly struct, `SliceFx.Core`) — status-only result for features
  whose success path carries no body (204 No Content). The generated C# client emits `Task` (void),
  avoiding a throw on empty-body deserialization. Both structs live in the `SliceFx` namespace with
  factory methods `Ok`, `Created`, `NoContent`, `NotFound`, `Unauthorized`, `BadRequest`, `Problem`.
- `SliceResultExtensions.ToWasiResponse<T>` / `ToWasiResponse` in `SliceFx.Wasi` — translates the
  new Core result structs to `WasiResponse` using the existing AOT-safe factory methods. `WasiResponse`-
  returning features remain supported as a raw escape hatch.
- `SliceFx.Wasi` now declares a dependency on `SliceFx.Core`; users referencing `SliceFx.Wasi`
  receive `SliceFx.Core` transitively.

## 0.1.0-preview.6 - 2026-05-31

### Fixed

- `slicefx client csharp`, `client typescript`, `openapi`: routes returning `WasiResponse` (a
  server-side transport record) no longer generate broken/uncompilable client methods. These routes
  are now excluded with a `// skipped (untyped WasiResponse): <name>` notice. Portability
  classification (`portable`) is unchanged. (Fixes #3)
- `slicefx client csharp`: null nullable scalar query parameters are now omitted from the generated
  query string (`if (param is not null)`) instead of being emitted as `"name="`. TypeScript client
  already had this guard. (Fixes #4)
- `WasiArgumentBinder.BindFromQuery<T>`: empty raw value for a nullable value-type (e.g. `int?`)
  now returns `Missing` instead of `Bound(null)`, aligning with the corrected client behaviour.
  `string?` is unaffected — empty string is a valid value for string parameters. (Fixes #4)

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
