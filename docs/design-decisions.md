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

1. **No per-request reflection, no startup-time reflection.** Anything that runs at request time must avoid reflection; the source generator emits the registration code, so `AddSlice()` and `MapSlices()` contain only direct method calls. The result is AOT-friendly (works under Native AOT publish without trim warnings) and avoids startup cost that scales with assembly count.
2. **Convention violations surface at compile time.** The generator emits 17 categories of diagnostics (SLICE001–017) — missing `Handle` method, ambiguous overloads, non-public handler, invalid filter type, unsupported WASI body binding, missing JsonContext for AOT, duplicate endpoint names across aggregated modules, and so on. A reflection-based scanner would either silently skip these cases or throw at startup.
3. **Tooling reuse.** The same generator emits a route manifest (`{Asm}_SliceRouteManifest.g.cs`) consumed by `slice routes` and `slice client csharp`. Reflection would require runtime introspection of a running app to produce the same output.

Implementation lives in `src/Slice.SourceGenerator/`. The pipeline is incremental — see "Why incremental?" below.

## Why an *incremental* generator? Doesn't every generator support that?

Yes, but the [`IIncrementalGenerator`](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview) API only avoids re-running on every keystroke if you wire it up correctly. Slice does:

- Uses `WithTrackingName` on every pipeline stage so cache behavior is testable.
- Reduces the `Compilation` to small, equatable record types **before** the final `RegisterSourceOutput` action, preventing cache busts when unrelated parts of the compilation change.
- `IncrementalCacheTests` in `tests/Slice.SourceGenerator.Tests` explicitly verifies that `SliceFeatureModels` and `SliceReferencedModules` stages report `Cached` / `Unchanged` on no-op edits.

The full perf baseline (M1, 200 features, 6.7 ms cold-run) is in [`production-readiness.md`](production-readiness.md), with **2× headroom gates enforced nightly** by `.github/workflows/perf.yml`.

## Why DataAnnotations *and* `ISliceValidator<T>`?

Most validation rules (`Required`, `MinLength`, `EmailAddress`) are declarative and read well on a record's primary constructor — DataAnnotations is the right tool. `DataAnnotationsValidationFilter` is attached automatically and runs first.

Cross-field rules, async checks (e.g., uniqueness against a store), or anything that needs DI don't fit attributes cleanly. For those, implement `ISliceValidator<T>` and attach `[Filter<SliceValidatorFilter<TRequest>>]`. The two compose: DataAnnotations runs first, and only on success does the filter chain (including `SliceValidatorFilter<T>`) execute.

Both APIs live in `Slice.Core` and require zero extra NuGet packages.

## Why does `[Filter<T>]` take a type parameter only — no constructor args?

The constraint is intentional and protects strength-preservation invariant #1 ("100% pure ASP.NET Core Minimal API expansion"). If `[Filter]` accepted instance state, the generator would have to either (a) lift values into a generated singleton — runtime magic — or (b) emit a closure that captures attribute values, breaking the "generated code only chains standard APIs" rule.

Instead, filters are scoped services. Configure them through constructor DI by binding `IOptions<T>` or any other service. See [`docs/patterns/filter-configuration.md`](patterns/filter-configuration.md) for the recipe.

## Why are some features classified `aspnet-only` and excluded from WASI?

Three diagnostics drive the classification (`portable` / `partial` / `aspnet-only`):

- **SLICE008**: Feature returns `IResult` / `Task<IResult>`. `IResult` is an ASP.NET-specific abstraction; WASI dispatch has no equivalent, so these features are correctly excluded.
- **SLICE009**: A body-binding route needs `WasiJsonContext` for AOT-safe deserialization on the WASI path. Without it, the route can't be added to `WasiRouteTable`.
- **SLICE011**: The route uses a DataAnnotations attribute (e.g., `Range`, `RegularExpression`) whose WASI implementation falls back to reflection. WASI excludes these to keep the route table reflection-free.

The classification is exposed in the route manifest, surfaced by `slice routes`, and consumed by both `slice client csharp` and the WASI source-generator path. The same vocabulary keeps tooling and runtime aligned.

## Why no CLI flags for "generate everything"? Why opt-in adapters?

Each satellite (`Slice.Lambda`, `Slice.TestHost`, `Slice.Wasi`) brings its own NuGet dependencies. Forcing them on every consumer would erode the zero-dep value of `Slice.Core` and pull in transitive packages that AOT publishers don't want. Opt-in by package reference keeps the dependency graph honest: if you reference `Slice.Wasi`, the WASI generator path activates; otherwise, the WASI emitter is skipped entirely and produces no output.

This is why the source generator emits up to three separate files (`{Asm}_SliceRegistrations.g.cs`, `{Asm}_SliceWasiRegistrations.g.cs`, `{Asm}_SliceRouteManifest.g.cs`) instead of one combined output.

## Why does the warm-run benchmark report slower than cold-run?

Honest answer: because the emit step is currently not cached. Upstream stages (`SliceFeatureModels`, `SliceReferencedModules`) are confirmed cached by `IncrementalCacheTests`, but `RegisterSourceOutput` re-runs the formatting/emit work each pass. Output text is byte-identical (verified by tests), so correctness is fine.

This is documented in [`production-readiness.md`](production-readiness.md) observation #2 as a candidate follow-up. Absolute cost at 200 features is ~9 ms — well below the nightly perf gate at 20 ms — so it's not blocking.

## Why publish a `slice` CLI as a .NET local tool?

A local tool (declared in `dotnet-tools.json`) version-pins itself per repository, so `slice routes` produces the same output everywhere from a fresh `dotnet tool restore`. A global tool would drift across developer machines; a project executable would mix tool versions into the build output. Local tools are the right scope for "scaffolding + route inspection + client generation."
