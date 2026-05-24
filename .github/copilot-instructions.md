# Repository instructions for Copilot

## Build, test, and lint commands

This repo targets .NET 10 via `global.json` (`10.0.300`, roll forward to latest feature). CI runs the solution file `Slice.slnx`.

```pwsh
dotnet restore Slice.slnx
dotnet build Slice.slnx --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test Slice.slnx --configuration Release --no-build --no-restore
dotnet format Slice.slnx --verify-no-changes --no-restore --severity info --exclude-diagnostics CS1591
```

Use `dotnet format Slice.slnx --no-restore` to apply formatting when intentional. The repo treats warnings and code-analysis diagnostics as errors through `Directory.Build.props`.

Run targeted tests by project or by xUnit filter:

```pwsh
dotnet test tests\Slice.SourceGenerator.Tests\Slice.SourceGenerator.Tests.csproj --configuration Release --filter "FullyQualifiedName~Registration"
dotnet test Slice.slnx --configuration Release --filter "FullyQualifiedName~CreateUser"
```

For user-facing behavior, run the affected sample and hit one route:

```pwsh
dotnet run --project samples\Slice.Sample
curl.exe http://localhost:5099/health
curl.exe -X POST http://localhost:5099/users -H "Content-Type: application/json" -d "{\"name\":\"Alice\",\"email\":\"alice@example.com\"}"
```

Other runnable samples:

```pwsh
dotnet run --project samples\Slice.LambdaSample       # local Kestrel run, Lambda-ready
dotnet run --project samples\Slice.TestHostSample     # in-process HTTP demo
dotnet publish samples\Slice.WasiSample -r wasi-wasm -c Release  # Linux x64 / Windows x64, or Docker linux/amd64 on macOS
```

CI also packs every library/tool project. Before finishing framework changes, verify `src\Slice.Core\Slice.Core.csproj` still has no `<PackageReference>` entries.

## High-level architecture

Slice is an experimental vertical-slice-first web framework on ASP.NET Core Minimal APIs: one feature file defines a route, request/response records, validation, filters, and a static handler.

- `src\Slice.Core` contains the runtime framework: `[Feature]`, `[Filter<T>]`, `DataAnnotationsValidationFilter`, `ISliceValidator<T>`, and `SliceValidatorFilter<T>`. It must stay dependency-free except for the `Microsoft.AspNetCore.App` framework reference.
- `src\Slice.SourceGenerator` is the sole registration path. It discovers `[Feature]` classes, validates route/handler shape with `SLICE###` diagnostics, emits `AddSlice`/`MapSlices` extension methods into the `Slice` namespace, and emits route manifests consumed by tooling.
- Generated and runtime endpoint registration order is significant: map method, attach `DataAnnotationsValidationFilter`, attach `[Filter<T>]` filters in declaration order, then apply tags, endpoint name, and summary.
- Feature assemblies emit module markers so host generators can aggregate referenced Slice modules without runtime scanning. `SliceAggregateReferences=false` disables aggregation; `SliceReferencedAssemblies` allow-lists referenced assembly simple names.
- `src\Slice.Lambda`, `src\Slice.TestHost`, and `src\Slice.Wasi` are satellite libraries that keep optional hosting/testing dependencies out of `Slice.Core`.
- `src\Slice.Wasi` bypasses ASP.NET/Kestrel and dispatches through a generated `WasiRouteTable`. The generator only emits WASI registrations when `Slice.Wasi.Routing.WasiRouteTable` is referenced, excludes ASP.NET-specific `IResult` routes with `SLICE008`, body-binding routes missing `WasiJsonContext` with `SLICE009`, and routes needing reflection-based DataAnnotations validation with `SLICE011`.
- WASI support is experimental, and publishing/deploying WASI components depends on unstable upstream preview tooling (`componentize-dotnet`, NativeAOT-LLVM, WASI Preview 2, and Cloudflare's `jco`/`preview2-shim`/Wrangler path). Keep the support matrix to Linux x64 / Windows x64 native publish, macOS via Linux x64 Docker, and unsupported-host fail-fast behavior.
- `tools\Slice.Cli` is the local `slice` scaffolding/tooling app. It reads generated route metadata when available, falls back to scanning `Features/**/*.cs`, lists portability (`portable`, `partial`, `aspnet-only`), generates typed C# clients, and can generate AWS Lambda SAM manifests.
- `samples\Slice.Sample` is the canonical app shape for ASP.NET features; the Lambda, TestHost, and Workers samples show adapter-specific bootstrapping.

## Key conventions

- Features are `public static class` types under a namespace containing `.Features.` and are annotated with `[Feature("METHOD /path", Summary = "...")]`.
- Every feature handler must be `public static Handle(...)`. Dependencies are method parameters and are resolved by Minimal API binding or DI; feature classes do not use constructor injection.
- Request DTOs usually live as nested `Request` records in a user namespace. DataAnnotations on record positional parameters are intentionally supported; validation failures return Problem Details.
- Tag inference uses the namespace segment immediately after `.Features.` (`Slice.Sample.Features.Users` -> `Users`). Endpoint names are `{Tag}.{FeatureClassName}`; use `FeatureAttribute.Tag` to disambiguate duplicates.
- `[Filter<T>]` types are plain ASP.NET Core `IEndpointFilter`s. `AddSlice()` registers referenced filters as scoped services. `DataAnnotationsValidationFilter` always runs before feature filters.
- Filter declaration order is meaningful. `FilterOrderHintAttribute` currently enforces `After = typeof(...)` preferences at compile time with `SLICE010`.
- `ISliceValidator<T>` implementations are not auto-scanned. Register them manually in DI, then attach `SliceValidatorFilter<Request>` with `[Filter<SliceValidatorFilter<Request>>]`.
- Keep per-request paths reflection-free. The generated `AddSlice`/`MapSlices` avoid all startup reflection; never re-introduce reflection-based scanning.
- `WebApplication.CreateSlimBuilder` is intentional in samples because the framework is trimming/AOT oriented.
- Do not introduce mediator-style abstractions (`IMediator`, `IPipelineBehavior`, etc.); cross-cutting behavior should use endpoint filters.
- WASI features should return `WasiResponse`, `SliceResult`, POCOs, `Task<T>`, or `ValueTask<T>`, not `IResult`. Non-validator `[Filter<T>]` filters do not run in the WASI path. Body-binding WASI routes must provide a source-generated `WasiJsonContext` to avoid `SLICE009`, and WASI DataAnnotations should stay within generated rules (`Required`, `StringLength`, supported `MinLength`) to avoid `SLICE011`.
- If changing registration, validation, filters, metadata, or diagnostics, update the relevant source generator emitter in `src\Slice.SourceGenerator\Emit\`.
- `Program.cs` files use top-level statements and may need `using Slice;` for `ISliceValidator<T>` because top-level statements are in the global namespace.
