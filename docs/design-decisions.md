# Design decisions FAQ

This page collects the design questions that come up most often about Slice, with short answers and pointers into the codebase. It complements [`production-readiness.md`](production-readiness.md), which documents the **strength-preservation invariants** these decisions defend.

## Why no `IMediator` / `IPipelineBehavior` layer?

Slice deliberately avoids introducing a mediator stack on top of ASP.NET Core. The reasoning:

- ASP.NET Core's `IEndpointFilter` already covers the cross-cutting concerns that a mediator pipeline would, with native support, scoped DI, and built-in OpenAPI integration. Adding a second pipeline is duplication.
- A mediator hides the per-feature dispatch path behind reflection or container resolution. Slice's source generator emits the dispatch path directly (`endpoints.MapMethods(pattern, [method], delegate)`), so it stays AOT-friendly and stack traces stay short.
- Per the adoption matrix in [`production-readiness.md`](production-readiness.md), teams already invested in MediatR / `IPipelineBehavior` are explicitly **not** the target audience. Slice optimizes for projects that want to keep Minimal API close.

If you want filter-style behavior, attach `[Filter<T>]`. If you want validation, attach `DataAnnotationsValidationFilter` (automatic) or implement `ISliceValidator<T>`. No mediator is needed for either.

## Why `WebApplication.CreateSlimBuilder` instead of `CreateBuilder`?

`CreateSlimBuilder` is the trimming-friendly host: it skips the default Razor / MVC services that aren't needed for an API-only app, which keeps the trimmed and AOT-published binary small. Since Slice's value proposition includes "ports cleanly to Lambda / WASI where small binaries matter," it's the right default. Switch to `CreateBuilder` only if your project actually needs the omitted services.

## Why is `Slice.Core` zero-dependency, and how is that enforced?

The goal is to keep the runtime surface area auditable and prevent supply-chain creep. The only allowed reference is `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.

It is enforced in **two** places, intentionally redundant:

1. **Local build**: `Directory.Build.targets` declares the `ValidateSliceCorePackageReferences` MSBuild target. Adding a `<PackageReference>` to `src/Slice.Core/Slice.Core.csproj` fails the build on every developer machine.
2. **CI**: `.github/workflows/ci.yml` has a final PowerShell step that re-checks the project file and fails the pipeline if `<PackageReference` appears. This catches the case where someone disables the MSBuild target.

Satellite projects (`Slice.SourceGenerator`, `Slice.Lambda`, `Slice.TestHost`, `Slice.Wasi`, `Slice.Cli`) are free to take NuGet dependencies. Only `Slice.Core` is constrained.

## Why a source generator? Why not reflection at startup?

Three reasons, in order of importance:

1. **Generated route discovery and dispatch avoid reflection.** Anything that runs at request time must avoid reflection; the source generator emits the route registration and dispatch code, so `AddSlice()` and `MapSlices()` contain direct method calls instead of assembly scanning. DataAnnotations validation still builds its ASP.NET validation plan at endpoint-build time. The generated path stays AOT-friendly and avoids route-discovery startup cost that scales with assembly count.
2. **Convention violations surface at compile time.** The generator emits 18 categories of diagnostics (SLICE001–006 and SLICE008–019) — missing `Handle` method, ambiguous overloads, non-public handler, invalid filter type, unsupported WASI body binding, missing JsonContext metadata for AOT, duplicate endpoint names across aggregated modules, and so on. A reflection-based scanner would either silently skip these cases or throw at startup.
3. **Tooling reuse.** The same generator emits a route manifest (`{Asm}_SliceRouteManifest.g.cs`) consumed by `slice routes` and `slice client csharp`. Reflection would require runtime introspection of a running app to produce the same output.

Implementation lives in `src/Slice.SourceGenerator/`. The pipeline is incremental — see "Why incremental?" below.

## Why `IIncrementalGenerator`?

The [`IIncrementalGenerator`](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview) API only avoids re-running work on every keystroke if the pipeline is wired with cacheable inputs. Slice does:

- Uses `WithTrackingName` on every pipeline stage so cache behavior is testable.
- Reduces the `Compilation` to small, equatable record types **before** the final `RegisterSourceOutput` action, preventing cache busts when unrelated parts of the compilation change.
- `IncrementalCacheTests` in `tests/Slice.SourceGenerator.Tests` explicitly verifies that `SliceFeatureModels` and `SliceReferencedModules` stages report `Cached` / `Unchanged` on no-op edits.

The full perf baseline (M1, 200 features, 6.7 ms cold-run) is in [`production-readiness.md`](production-readiness.md), with **2× headroom gates enforced nightly** by `.github/workflows/perf.yml`.

## Why DataAnnotations *and* `ISliceValidator<T>`?

Most validation rules (`Required`, `MinLength`, `EmailAddress`) are declarative and read well on a record's primary constructor — DataAnnotations is the right tool. `DataAnnotationsValidationFilter` is attached automatically and runs first.

Cross-field rules, async checks (e.g., uniqueness against a store), or anything that needs DI don't fit attributes cleanly. For those, implement one closed `ISliceValidator<TRequest>` where `TRequest` is a discovered Slice request parameter. The generator registers it and runs it automatically after DataAnnotations and before user-declared `[Filter<T>]` filters; unmatched validators are reported as build errors.

Both APIs live in `Slice.Core` and require zero extra NuGet packages.

## Why does `[Filter<T>]` take a type parameter only — no constructor args?

The constraint is intentional and protects strength-preservation invariant #1 ("100% pure ASP.NET Core Minimal API expansion"). If `[Filter]` accepted instance state, the generator would have to either (a) lift values into a generated singleton — runtime magic — or (b) emit a closure that captures attribute values, breaking the "generated code only chains standard APIs" rule.

Instead, filters are scoped services. Configure them through constructor DI by binding `IOptions<T>` or any other service. See [`docs/patterns/filter-configuration.md`](patterns/filter-configuration.md) for the recipe.

## Why are some features classified `aspnet-only` and excluded from WASI?

WASI exclusions and route-manifest portability use the same vocabulary, but they are checked at different layers:

- **Route manifest portability**: `aspnet-only` is used when a feature returns `IResult` / `Task<IResult>` (SLICE008), and `partial` is used when reflection-based DataAnnotations validation (SLICE011) or endpoint filters prevent full WASI behavior.
- **WASI route table emission**: JSON body/response routes additionally need a source-generated `JsonSerializerContext` marked with `[SliceJsonContext(SliceJsonTarget.Wasi)]` for AOT-safe serialization. Without it, SLICE009 is reported and the route is skipped from `WasiRouteTable`, even though the manifest portability classification is computed separately.

The manifest classification is surfaced by `slice routes` and consumed by `slice client csharp`; the WASI source-generator path applies its own route-table eligibility checks using the same portability vocabulary where applicable.

## Why no CLI flags for "generate everything"? Why opt-in adapters?

Each satellite (`Slice.Lambda`, `Slice.TestHost`, `Slice.Wasi`) brings its own NuGet dependencies. Forcing them on every consumer would erode the zero-dep value of `Slice.Core` and pull in transitive packages that AOT publishers don't want. Opt-in by package reference keeps the dependency graph honest: if you reference `Slice.Wasi`, the WASI generator path activates; otherwise, the WASI emitter is skipped entirely and produces no output.

`Slice.Wasi` is still experimental, and publishing a WASI component also depends on preview upstream tooling (`componentize-dotnet`, NativeAOT-LLVM, WASI Preview 2, and Cloudflare's JS shim path when targeting Workers). Keeping it as an opt-in satellite makes that toolchain risk explicit instead of imposing it on ASP.NET-only apps.

This is why the source generator emits separate files for each active surface (`{Asm}_SliceRegistrations.g.cs`, `{Asm}_SliceWasiRegistrations.g.cs`, `{Asm}_SliceRouteManifest.g.cs`, and `{Asm}.SliceLambdaPerFunctionHandlers.g.cs` when Lambda per-feature handlers are enabled) instead of one combined output.

## How is warm-run kept faster than cold-run?

Upstream stages (`SliceFeatureModels`, `SliceReferencedModules`) are confirmed cached by `IncrementalCacheTests`, and final source generation is folded into a cacheable `SliceEmitPlan` step. `RegisterSourceOutput` stays thin: it reports structural diagnostics and adds cached source text, but it does not rebuild route manifests or registration source when inputs are unchanged.

The benchmark suite separates `CompilationEditOnly`, `WarmRun_NoOpEdit`, and `WarmRun_TrackedTreeTrivialEdit` so the measured warm path reflects generator reuse rather than syntax-tree replacement cost. The cached emit plan intentionally retains generated source strings in Roslyn's incremental state table; the trade-off is small steady-state memory for lower per-edit work.

## Why publish a `slice` CLI as a .NET local tool?

A local tool (declared in `dotnet-tools.json`) version-pins itself per repository, so `slice routes` produces the same output everywhere from a fresh `dotnet tool restore`. A global tool would drift across developer machines; a project executable would mix tool versions into the build output. Local tools are the right scope for "scaffolding + route inspection + client generation."
