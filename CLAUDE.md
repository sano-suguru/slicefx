# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Slice is an experimental .NET web framework built on ASP.NET Core Minimal API that makes Vertical Slice Architecture the primary unit: **1 file = 1 feature = 1 deploy unit**. The runtime framework lives in `src/Slice.Core/`; the optional Roslyn source generator lives in `src/Slice.SourceGenerator/`. `samples/Slice.Sample/` is the reference for how user code should look.

This is pre-1.0 experimental software. Source Generator / AOT, TestHost, Lambda, fluent validation, Workers, and CLI scaffolding are implemented experimentally, but preview packages should use `0.x` versions until the public API is intentionally stabilized. CI (`.github/workflows/ci.yml`) runs restore, Release build, tests, package verification, a `dotnet format --verify-no-changes --severity info` gate, and a guard that fails if `Slice.Core` gains a `<PackageReference>`. A second workflow (`pages.yml`) publishes `docs/` to GitHub Pages. Targets **.NET 10** (`Directory.Build.props` pins `<TargetFramework>net10.0</TargetFramework>` with `LangVersion=latest`; the source generator stays on `netstandard2.0` as Roslyn requires). SDK pinned via `global.json` to `10.0.300` (`rollForward: latestFeature`).

## Commands

```bash
dotnet build
dotnet test Slice.slnx --configuration Release --no-build --no-restore
dotnet run --project samples/Slice.Sample        # listens on http://localhost:5099
dotnet run --project samples/Slice.LambdaSample  # listens on http://localhost:5100 (Lambda-ready)
dotnet run --project samples/Slice.TestHostSample # in-process HTTP demo (no server port)
dotnet publish samples/Slice.WorkersSample -r wasi-wasm -c Release  # Linux x64 / Windows x64 publish host
dotnet format                                     # canonical formatter (no config file)
```

xUnit tests live under `tests/`: `Slice.Core.Tests`, `Slice.SourceGenerator.Tests`, `Slice.Workers.Tests`, and `Slice.Cli.Tests`. CI runs `dotnet test Slice.slnx` (whole solution); use `dotnet test tests/<Name>` to target one project, or `dotnet test Slice.slnx --filter "FullyQualifiedName~<Substring>"` for a single test. Also smoke-test the main sample when behavior changes (app must be running):

```bash
curl http://localhost:5099/health
curl -X POST http://localhost:5099/users -H "Content-Type: application/json" \
  -d '{"name":"Alice","email":"alice@example.com"}'
curl -X DELETE http://localhost:5099/users/{id} -H "X-API-Key: secret"
```

## Hard constraints

- **Slice.Core stays zero-dependency.** `src/Slice.Core/Slice.Core.csproj` must never gain a `<PackageReference>`. Enforced in two places: an MSBuild target `ValidateSliceCorePackageReferences` in `Directory.Build.targets` that fails the build locally, and a PowerShell step in `.github/workflows/ci.yml` that fails CI. The only allowed reference is `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. Satellite projects (`src/Slice.SourceGenerator`, `src/Slice.Lambda`, `src/Slice.TestHost`, `src/Slice.Workers`, `tools/Slice.Cli`, and their samples) restore from nuget.org normally.
- **No new framework abstractions.** Slice intentionally avoids `IPipelineBehavior`, `IMediator`, etc. Cross-cutting concerns reuse ASP.NET Core's `IEndpointFilter`.
- **No per-request reflection.** Anything that runs per-request must avoid reflection. The source generator emits `AddSlice` / `MapSlices` — the sole registration path — which avoids startup reflection entirely (AOT-friendly).
- **`WebApplication.CreateSlimBuilder` is intentional** (trimming-friendly host). Do not switch to `CreateBuilder` without reason.
- **Experimental satellite scope.** Source generator, AWS Lambda adapter, TestHost, fluent validator (`ISliceValidator<T>`), CLI scaffolding, and Cloudflare Workers adapter (`Slice.Workers`) are all implemented experimentally. `Slice.Workers` provides in-process dispatch, stdin/stdout IPC, and a componentize-dotnet WASI publish path for Cloudflare Workers. Do not use the `wasi-experimental` Mono workload for this codebase.
- **Style is CI-enforced.** `.editorconfig` mandates file-scoped namespaces, `var` usage, 4-space indent (2 for JSON/YAML), final newline, LF line endings. Several IDE diagnostics (IDE0005, IDE0055, IDE0060, IDE0161, IDE0300/0301/0305/0306) are elevated to warning. CI's `dotnet format --severity info` will fail on any violation.

## Authoring a feature (the pattern all samples follow)

A feature is one `public static class` per file under `<App>.Features.<Group>.<FeatureName>`, with nested `Request`/`Response` records and a `public static [async] Task<Response> Handle(...)` method by default. Return `IResult` only when the feature intentionally needs ASP.NET-specific response helpers.

```csharp
// samples/Slice.Sample/Features/Users/CreateUser.cs
namespace Slice.Sample.Features.Users;

[Feature("POST /users", Summary = "Create a new user")]
[Filter<RequestLoggingFilter>]   // optional; declaration order = outermost first
public static class CreateUser
{
    public record Request(
        [Required, MinLength(2)] string Name,
        [Required, EmailAddress] string Email);

    public record Response(Guid Id, string Name, string Email, DateTime CreatedAt);

    public static async Task<Response> Handle(Request req, IUserStore store, CancellationToken ct)
    {
        var user = await store.AddAsync(req.Name, req.Email, ct);
        return new Response(user.Id, user.Name, user.Email, user.CreatedAt);
    }
}
```

Framework-enforced conventions (not just style):

- `Handle` **must be `public static`** — the source generator emits SLICE001/SLICE002/SLICE005 diagnostics (errors) if absent, non-public, non-static, or ambiguous. Dependencies arrive as parameters; Minimal API binds them from DI, route, query, or body automatically.
- Request records **must live in a user namespace** — `DataAnnotationsValidationFilter` skips types whose namespace starts with `System` or `Microsoft`, so a request typed as a BCL type will not be validated.
- DataAnnotations on **record positional parameters** are honored (the validator reads attrs from both properties and matching primary-ctor parameters). Validation failures return Problem Details automatically.
- OpenAPI tag is inferred from the namespace segment after `.Features.` (`Slice.Sample.Features.Users` → tag `Users`); endpoint name is `{Tag}.{TypeName}`. Override via `[Feature(..., Tag = "...")]`.
- Filters are plain `IEndpointFilter` classes under `Filters/`. `AddSlice()` discovers every `[Filter<T>]` reference and registers `T` as **scoped** in DI. `DataAnnotationsValidationFilter` is always attached before any `[Filter<T>]`.

## Satellite libraries

### ISliceValidator&lt;T&gt; — fluent validation (`src/Slice.Core/`)

`ISliceValidator<T>`, `SliceValidationResult`, and `SliceValidatorFilter<T>` are defined in `Slice.Core`
(no extra NuGet dependency). Use when DataAnnotations alone isn't expressive enough (cross-field rules,
async checks, etc.). `DataAnnotationsValidationFilter` is attached first; `SliceValidatorFilter<T>`
then runs in normal `[Filter<T>]` declaration order with any other feature filters.

```csharp
// 1. Implement the validator
public sealed class MyRequestValidator : ISliceValidator<MyFeature.Request>
{
    public ValueTask<SliceValidationResult> ValidateAsync(MyFeature.Request value, CancellationToken ct)
    {
        if (value.Name == "banned")
            return ValueTask.FromResult(SliceValidationResult.Failure("Name", "Name is not allowed."));
        return ValueTask.FromResult(SliceValidationResult.Success);
    }
}

// 2. Attach to the feature
[Feature("POST /things")]
[Filter<SliceValidatorFilter<Request>>]   // runs after DataAnnotations, in [Filter<T>] declaration order
public static class MyFeature { ... }

// 3. Register with DI (in Program.cs — generator does NOT auto-scan ISliceValidator<T>)
builder.Services.AddScoped<ISliceValidator<MyFeature.Request>, MyRequestValidator>();
```

Both DataAnnotations and `ISliceValidator<T>` can coexist on the same feature; DataAnnotations runs
first, and only if it passes does the `[Filter<T>]` chain continue. Put
`[Filter<SliceValidatorFilter<Request>>]` before or after other filters to control whether those
filters run before or after custom validation. Note: `Program.cs` (top-level statements) runs in
the global namespace — add `using Slice;` to access `ISliceValidator<T>` directly.

### Slice.Workers (`src/Slice.Workers/`)

ASP.NET-independent Workers satellite (experimental). Bypasses Kestrel entirely; the source generator emits a second output file (`<Asm>.SliceWorkersRegistrations.g.cs`) containing `RegisterWorkerRoutes(WorkerRouteTable)` and `AddSlice(WorkerHostBuilder)`. Emitted only when `Slice.Workers.Routing.WorkerRouteTable` is present in the compilation, so existing projects are unaffected.

**In-process dispatch and IPC:** `WorkerApp.DispatchAsync(WorkerRequest)` routes requests in-process through the source-generated `WorkerRouteTable`. `WorkerApp.Run()` runs the synchronous JSON-lines stdin/stdout IPC loop used by WASI command hosts; `RunAsync()` remains available for non-WASI hosts.

**WASI publish:** `samples/Slice.WorkersSample` publishes through [componentize-dotnet](https://github.com/bytecodealliance/componentize-dotnet) (NativeAOT-LLVM + WASI Preview 2 Component Model): `dotnet publish samples/Slice.WorkersSample -r wasi-wasm -c Release`. With the current NativeAOT-LLVM preview packages, publish is supported from Linux x64 or Windows x64 hosts; macOS can run the in-process probe but intentionally fails publish with a clear MSBuild error. The sample copies the generated component to `samples/Slice.WorkersSample/worker/slice-workers-sample.wasm`; `samples/Slice.WorkersSample/worker` then uses `@bytecodealliance/jco` and `@bytecodealliance/preview2-shim` to transpile the component for the Cloudflare shim (`npm install`, then `npm run build`).

Features returning `IResult`/`Task<IResult>` are excluded from Workers routes automatically (SLICE008 info diagnostic). Workers DataAnnotations validation is source-generated for supported `Required`, `StringLength`, and `MinLength` rules; routes that need reflection-based validation are excluded with SLICE011. `[Filter<T>]` filters other than `SliceValidatorFilter<T>` are not executed in the Workers path (they require ASP.NET's `IEndpointFilter` pipeline).

```csharp
var builder = WorkerHost.CreateBuilder();
builder.AddSlice();                       // source-generated route wiring
builder.Services.AddSingleton(TimeProvider.System);
var app = builder.Build();
await app.DispatchAsync(request);         // in-process dispatch
```

### Slice.Lambda (`src/Slice.Lambda/`)

Two extension methods. `UseSliceLambda()` calls `AddAWSLambdaHosting()` (from
`Amazon.Lambda.AspNetCoreServer.Hosting` v2.x), which auto-detects the Lambda runtime via
`LAMBDA_TASK_ROOT`; it is a no-op locally so Kestrel handles requests as normal.

```csharp
builder.Services.AddSlice();
builder.UseSliceLambda();               // no-op locally; activates Lambda runtime in Lambda
var app = builder.Build();
app.MapSlices();
await app.RunOnLambdaAsync();           // delegates to app.RunAsync()
```

The default event source is `LambdaEventSource.HttpApi` (API Gateway HTTP API v2). Pass
`LambdaEventSource.RestApi` or `LambdaEventSource.ApplicationLoadBalancer` to override.
See `samples/Slice.LambdaSample/` for a complete working example.

### Slice.TestHost (`src/Slice.TestHost/`)

Creates an in-process test server via `WebApplicationFactory<TEntryPoint>`. The optional
`configure` callback runs after the app's own service registrations, which is where you swap
out DI registrations for test doubles. `global::Program` reaches the target app's `Program`
class (made public via a `public partial class Program { }` declaration in `TestSupport.cs`).

```csharp
await using var host = SliceTestHost.Create<global::Program>(svc =>
    svc.Replace<IUserStore>(new FakeUserStore()));

var resp = await host.Client.PostAsJsonAsync("/users", new { name = "Alice", email = "a@b.c" });
```

`IServiceCollection.Replace<TService, TImpl>(lifetime)` and `Replace<TService>(instance)` are
provided by `Slice.Testing.ServiceCollectionExtensions`. See
`samples/Slice.TestHostSample/` for a runnable demo.

### Slice.Cli (`tools/Slice.Cli/`)

Local .NET tool (`dotnet-tools.json` at the repo root) exposing the `slice` command. Built on `System.CommandLine`. Verbs:

- `slice new feature <Name>` / `slice new filter <Name>` — scaffolds a feature class or `IEndpointFilter` from `Templates/`.
- `slice routes [--format table|json]` — lists every feature in the project plus its **portability classification** (`portable` / `partial` / `aspnet-only`). Reads feature source files directly.
- `slice client csharp --output <path>` — generates a typed C# client from the same manifest.

Features returning `IResult`/`Task<IResult>` are classified `aspnet-only` and excluded from Workers routes (matches diagnostic SLICE008).

## Startup pipeline

`src/Slice.SourceGenerator/Emit/RegistrationEmitter.cs` emits the registration code. Read it before changing registration behavior.

`AddSlice()` / `MapSlices()` are generated extension methods emitted into the `Slice` namespace for host projects. Feature assemblies also emit public non-extension module helpers and assembly markers so host generators can aggregate referenced feature assemblies without runtime scanning and validate endpoint-name uniqueness across aggregated modules. `SliceAggregateReferences=false` disables referenced module aggregation; `SliceReferencedAssemblies` allow-lists referenced assembly simple names. `AddSlice()` registers every filter referenced by `[Filter<T>]` as a scoped service. `MapSlices()` maps each `[Feature]` class: calls `endpoints.MapMethods(pattern, [method], delegate)`, attaches `DataAnnotationsValidationFilter`, then each `[Filter<T>]` in declaration order, then sets tag/summary/name.

The source generator emits up to three files per assembly: `{AsmName}_SliceRegistrations.g.cs` (ASP.NET registrations, when `Microsoft.AspNetCore.Http.IResult` is referenced), `{AsmName}_SliceWorkersRegistrations.g.cs` (Workers registrations, only when `Slice.Workers.Routing.WorkerRouteTable` is referenced), and `{AsmName}_SliceRouteManifest.g.cs` — a `SliceRouteDescriptor` record plus `GetSliceRoutesGenerated()` consumed by tooling. The manifest includes the shared portability vocabulary (`portable`, `partial`, `aspnet-only`) and is emitted regardless of which hosting path is referenced. Emitter source: `src/Slice.SourceGenerator/Emit/`.

## Repo layout

```
Slice.slnx
global.json               # SDK pin 10.0.300 (rollForward: latestFeature)
Directory.Build.props     # net10.0 TFM, LangVersion=latest, TreatWarningsAsErrors, EnforceCodeStyleInBuild
Directory.Build.targets   # ValidateSliceCorePackageReferences guard (zero-dep enforcement)
.editorconfig             # file-scoped namespaces; CI enforces via dotnet format
src/Slice.Core/           # the framework: FeatureAttribute, FilterAttribute,
                          # DataAnnotationsValidationFilter, ISliceValidator<T>
src/Slice.SourceGenerator/# Roslyn generator emitting AddSlice/MapSlices into namespace Slice
src/Slice.Lambda/         # AWS Lambda hosting adapter over Amazon.Lambda.AspNetCoreServer.Hosting
src/Slice.TestHost/       # in-process test host wrapper over Microsoft.AspNetCore.Mvc.Testing
src/Slice.Workers/        # Cloudflare Workers / WASI adapter (ASP.NET-independent)
                          # WorkerHost, WorkerApp, WorkerRouteTable, SliceResult
tools/Slice.Cli/          # .NET tool: slice new feature|filter, slice routes, slice client csharp
tests/                    # xUnit: Slice.Core.Tests, Slice.SourceGenerator.Tests,
                          # Slice.Workers.Tests, Slice.Cli.Tests
docs/                     # published to GitHub Pages via .github/workflows/pages.yml
README.md, CHANGELOG.md, CONTRIBUTING.md, CODE_OF_CONDUCT.md, SECURITY.md, LICENSE
samples/Slice.Sample/
  Program.cs              # bootstrap: AddSlice → MapSlices → Run
  appsettings.json        # port 5099
  Features/<Group>/*.cs   # one feature per file
  Filters/*.cs            # IEndpointFilter implementations
  Services/               # demo IUserStore / InMemoryUserStore
samples/Slice.LambdaSample/   # Lambda-ready sample: AddSlice → UseSliceLambda → MapSlices → RunOnLambdaAsync
samples/Slice.TestHostSample/ # in-process HTTP demo against Slice.Sample
samples/Slice.WorkersSample/  # Workers/WASI sample: WorkerHost.CreateBuilder → AddSlice → RunAsync
  Features/               # Health (GET /health) and Echo (POST /echo)
  worker/                 # P2: dev-server.mjs (wasmtime bridge); P3: shim.mjs + wrangler.toml
```

Mixed-language comments are acceptable (the sample contains a Japanese comment in `Program.cs`).
