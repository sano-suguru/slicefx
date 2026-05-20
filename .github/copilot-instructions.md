# Repository instructions for Copilot

## Build, test, and lint commands

This repo targets .NET 10 via `global.json` (`10.0.201`, roll forward to latest feature). CI runs the solution file `Slice.slnx`.

```pwsh
dotnet restore Slice.slnx
dotnet build Slice.slnx --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test Slice.slnx --configuration Release --no-build --no-restore
dotnet format Slice.slnx --verify-no-changes --no-restore --severity info --exclude-diagnostics CS1591
```

Use `dotnet format` to apply formatting when intentional. The repo treats warnings and code-analysis diagnostics as errors through `Directory.Build.props`.

There is a focused xUnit project under `tests\Slice.Core.Tests`. For single-behavior smoke checks, run the affected sample and hit one route:

```pwsh
dotnet run --project samples\Slice.Sample
curl.exe http://localhost:5099/health
curl.exe -X POST http://localhost:5099/users -H "Content-Type: application/json" -d "{\"name\":\"Alice\",\"email\":\"alice@example.com\"}"
```

Other runnable samples:

```pwsh
dotnet run --project samples\Slice.LambdaSample       # local Kestrel run, Lambda-ready
dotnet run --project samples\Slice.TestHostSample     # in-process HTTP demo
dotnet run --project samples\Slice.WorkersSample -- --probe /health
```

Before finishing framework changes, verify `src\Slice.Core\Slice.Core.csproj` still has no `<PackageReference>` entries.

## High-level architecture

Slice is a vertical-slice-first web framework on ASP.NET Core Minimal APIs: one feature file defines a route, request/response records, validation, filters, and a static handler.

- `src\Slice.Core` contains the runtime framework: `[Feature]`, `[Filter<T>]`, `AddSlice`/`MapSlices`, `DataAnnotationsValidationFilter`, `ISliceValidator<T>`, and `SliceValidatorFilter<T>`. It must stay dependency-free except for the `Microsoft.AspNetCore.App` framework reference.
- `src\Slice.SourceGenerator` is the AOT-friendly path. It discovers `[Feature]` classes, validates route/handler shape with `SLICE###` diagnostics, and emits `Slice.Generated` extension methods. `RegistrationEmitter` must mirror the runtime fallback in `SliceExtensions`.
- Generated and runtime endpoint registration order is significant: map method, attach `DataAnnotationsValidationFilter`, attach `[Filter<T>]` filters in declaration order, then apply tags, endpoint name, and summary.
- `src\Slice.Lambda`, `src\Slice.TestHost`, and `src\Slice.Workers` are satellite libraries that keep optional hosting/testing dependencies out of `Slice.Core`.
- `src\Slice.Workers` bypasses ASP.NET/Kestrel and dispatches through a generated `WorkerRouteTable`. The generator only emits Workers registrations when `Slice.Workers.Routing.WorkerRouteTable` is referenced, and it excludes ASP.NET-specific `IResult` routes with `SLICE008`.
- `tools\Slice.Cli` is the local `slice` scaffolding tool. It reads project `RootNamespace`, infers feature groups from names, and writes under `Features\<Group>\`.
- `samples\Slice.Sample` is the canonical app shape for ASP.NET features; the Lambda, TestHost, and Workers samples show adapter-specific bootstrapping.

## Key conventions

- Features are `public static class` types under a namespace containing `.Features.` and are annotated with `[Feature("METHOD /path", Summary = "...")]`.
- Every feature handler must be `public static Handle(...)`. Dependencies are method parameters and are resolved by Minimal API binding or DI; feature classes do not use constructor injection.
- Request DTOs usually live as nested `Request` records. DataAnnotations on record positional parameters are intentionally supported; validation failures return Problem Details.
- Tag inference uses the namespace segment immediately after `.Features.` (`Slice.Sample.Features.Users` -> `Users`). Endpoint names are `{Tag}.{FeatureClassName}`; use `FeatureAttribute.Tag` to disambiguate duplicates.
- `[Filter<T>]` types are plain ASP.NET Core `IEndpointFilter`s. `AddSliceGenerated()`/`AddSlice()` registers referenced filters as scoped services. `DataAnnotationsValidationFilter` always runs before feature filters.
- `ISliceValidator<T>` implementations are not auto-scanned. Register them manually in DI, then attach `SliceValidatorFilter<Request>` with `[Filter<SliceValidatorFilter<Request>>]`.
- Keep per-request paths reflection-free. Runtime fallback reflection is startup-only and builds strongly typed delegates; generated registrations avoid startup reflection for AOT/trimming scenarios.
- `WebApplication.CreateSlimBuilder` is intentional in samples because the framework is trimming/AOT oriented.
- Do not introduce mediator-style abstractions (`IMediator`, `IPipelineBehavior`, etc.); cross-cutting behavior should use endpoint filters.
- Workers features should return `WorkerResponse`, `SliceResult`, POCOs, `Task<T>`, or `ValueTask<T>`, not `IResult`. Non-validator `[Filter<T>]` filters do not run in the Workers path.
- If changing registration, validation, filters, metadata, or diagnostics, update both `src\Slice.Core\SliceExtensions.cs` and the relevant source generator emitter so fallback and generated behavior stay aligned.
- `Program.cs` files use top-level statements and may need `using Slice;` for `ISliceValidator<T>` because top-level statements are in the global namespace.
