# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

SliceFx is an experimental .NET web framework built on ASP.NET Core Minimal API that makes Vertical Slice Architecture the primary unit: **1 file = 1 feature = 1 deploy unit**. The runtime framework lives in `src/SliceFx.Core/`; the optional Roslyn source generator lives in `src/SliceFx.SourceGenerator/`. `samples/SliceFx.Sample/` is the reference for how user code should look.

This is pre-1.0 experimental software. Source Generator / AOT, TestHost, Lambda, fluent validation, WASI, and CLI scaffolding are implemented experimentally, but preview packages should use `0.x` versions until the public API is intentionally stabilized. WASI publishing also depends on unstable upstream preview tooling; do not present componentize-dotnet, NativeAOT-LLVM, `jco`, `preview2-shim`, Wrangler, or Cloudflare runtime behavior as SliceFx-controlled guarantees. CI (`.github/workflows/ci.yml`) runs restore, Release build, tests, package verification, a `dotnet format --verify-no-changes --severity info` gate, and a guard that fails if `src/SliceFx.Core/SliceFx.Core.csproj` gains a `<PackageReference>`. A second workflow (`pages.yml`) publishes `docs/` to GitHub Pages. Targets **.NET 10** (`Directory.Build.props` pins `<TargetFramework>net10.0</TargetFramework>` with `LangVersion=latest`; the source generator stays on `netstandard2.0` as Roslyn requires). SDK pinned via `global.json` to `10.0.300` (`rollForward: latestFeature`).

## Commands

```bash
dotnet build
dotnet test SliceFx.slnx --configuration Release --no-build --no-restore
dotnet run --project samples/SliceFx.Sample        # listens on http://localhost:5099
dotnet run --project samples/SliceFx.LambdaSample  # listens on http://localhost:5100 (Lambda-ready)
dotnet run --project samples/SliceFx.TestHostSample # in-process HTTP demo (no server port)
dotnet publish samples/SliceFx.WasiSample -r wasi-wasm -c Release  # Linux x64 / Windows x64, or Docker linux/amd64 on macOS
dotnet format                                     # canonical formatter (no config file)
```

xUnit tests live under `tests/`: `SliceFx.Core.Tests`, `SliceFx.SourceGenerator.Tests`, `SliceFx.Wasi.Tests`, and `SliceFx.Cli.Tests`. CI runs `dotnet test SliceFx.slnx` (whole solution); use `dotnet test tests/<Name>` to target one project, or `dotnet test SliceFx.slnx --filter "FullyQualifiedName~<Substring>"` for a single test. Also smoke-test the main sample when behavior changes (app must be running):

```bash
curl http://localhost:5099/health
curl -X POST http://localhost:5099/users -H "Content-Type: application/json" \
  -d '{"name":"Alice","email":"alice@example.com"}'
curl -X DELETE http://localhost:5099/users/{id} -H "X-API-Key: secret"
```

## Hard constraints

- **SliceFx.Core stays zero-dependency.** `src/SliceFx.Core/SliceFx.Core.csproj` must never gain a `<PackageReference>`. Enforced in two places: an MSBuild target `ValidateSliceCorePackageReferences` in `Directory.Build.targets` that fails the build locally, and a PowerShell step in `.github/workflows/ci.yml` that fails CI. The only allowed reference is `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. Satellite projects (`src/SliceFx.SourceGenerator`, `src/SliceFx.Lambda`, `src/SliceFx.TestHost`, `src/SliceFx.Wasi`, `tools/SliceFx.Cli`, and their samples) restore from nuget.org normally.
- **No new framework abstractions.** SliceFx intentionally avoids `IPipelineBehavior`, `IMediator`, etc. Cross-cutting concerns reuse ASP.NET Core's `IEndpointFilter`.
- **No per-request reflection.** Anything that runs per-request must avoid reflection. The source generator emits `AddSlice` / `MapSlices` — the sole registration path — which avoids startup reflection entirely (AOT-friendly).
- **`WebApplication.CreateSlimBuilder` is intentional** (trimming-friendly host). Do not switch to `CreateBuilder` without reason.
- **Experimental satellite scope.** Source generator, AWS Lambda adapter, TestHost, fluent validator (`ISliceValidator<T>`), CLI scaffolding, and Cloudflare WASI adapter (`SliceFx.Wasi` package) are all implemented experimentally. `SliceFx.Wasi` provides in-process dispatch and a componentize-dotnet WASI publish path targeting `wasi:http/incoming-handler@0.2.0` — deployable to Cloudflare Workers (via jco) and Fermyon Cloud / Spin (natively). Do not use the `wasi-experimental` Mono workload for this codebase.
- **Style is CI-enforced.** `.editorconfig` mandates file-scoped namespaces, `var` usage, 4-space indent (2 for JSON/YAML), final newline, LF line endings. Several IDE diagnostics (IDE0005, IDE0055, IDE0060, IDE0161, IDE0300/0301/0305/0306) are elevated to warning. CI's `dotnet format --severity info` will fail on any violation.

## Authoring a feature (the pattern all samples follow)

A feature is one `public static class` per file under `<App>.Features.<Group>.<FeatureName>`, with nested `Request`/`Response` records and a `public static [async] Task<Response> Handle(...)` method by default. Return `IResult` only when the feature intentionally needs ASP.NET-specific response helpers.

```csharp
// samples/SliceFx.Sample/Features/Users/CreateUser.cs
namespace SliceFx.Sample.Features.Users;

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

- `Handle` **must be `public static`** — the source generator emits SLICE001/SLICE002/SLICE003 diagnostics (errors) if absent, non-public, non-static, or ambiguous. Dependencies arrive as parameters; Minimal API binds them from DI, route, query, or body automatically.
- Request records **must live in a user namespace** — generated validation skips framework types whose namespace starts with `System` or `Microsoft`, so a request typed as a BCL type will not be validated.
- DataAnnotations on **record positional parameters** are honored (the validator reads attrs from both properties and matching primary-ctor parameters). Validation failures return Problem Details automatically.
- OpenAPI tag is inferred from the namespace segment after `.Features.` (`SliceFx.Sample.Features.Users` → tag `Users`); endpoint name is `{Tag}.{TypeName}`. Override via `[Feature(..., Tag = "...")]`.
- Filters are plain `IEndpointFilter` classes under `Filters/`. `AddSlice()` discovers every `[Filter<T>]` reference and registers `T` as **scoped** in DI. Generated DataAnnotations validation is always attached before any `[Filter<T>]`.

## Satellite libraries

### ISliceValidator&lt;T&gt; — fluent validation (`src/SliceFx.Core/`)

`ISliceValidator<T>` and `SliceValidationResult` are defined in the `SliceFx.Core` package
(no extra NuGet dependency). Use when DataAnnotations alone isn't expressive enough (cross-field rules,
async checks, etc.). Implement one closed validator for a discovered Slice request parameter.
Generated DataAnnotations validation is attached first; `ISliceValidator<T>` then runs before
user-declared `[Filter<T>]` endpoint filters. Unmatched validators are build errors.

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

// The source generator discovers and registers the validator automatically.
```

Both DataAnnotations and `ISliceValidator<T>` can coexist on the same feature; DataAnnotations runs
first, then Slice validators, and only if they pass does the `[Filter<T>]` chain continue.

### SliceFx.Wasi (`src/SliceFx.Wasi/`)

ASP.NET-independent WASI satellite (experimental). Bypasses Kestrel entirely; the source generator emits a second output file (`<Asm>.SliceWasiRegistrations.g.cs`) containing `RegisterWasiRoutes(WasiRouteTable)` and `AddSlice(WasiHostBuilder)`. Emitted only when `SliceFx.Wasi.Routing.WasiRouteTable` is present in the compilation, so existing projects are unaffected.

**In-process dispatch:** `WasiApp.DispatchAsync(WasiRequest)` routes requests in-process through the source-generated `WasiRouteTable`. (`WasiApp.Run()` / `RunAsync()` have been removed; they were a JSON-lines CLI IPC mechanism superseded by the wasi:http approach.)

**WASI publish:** `samples/SliceFx.WasiSample` publishes through [componentize-dotnet](https://github.com/bytecodealliance/componentize-dotnet) (NativeAOT-LLVM + WASI Preview 2 Component Model): `dotnet publish samples/SliceFx.WasiSample -r wasi-wasm -c Release`. The component exports `wasi:http/incoming-handler@0.2.0` — the standard WASI HTTP interface. With the current NativeAOT-LLVM preview packages, native publish is supported from Linux x64 or Windows x64 hosts; macOS should publish through a Linux x64 Docker container such as `docker run --rm --platform linux/amd64 -v "$PWD":/work -w /work mcr.microsoft.com/dotnet/sdk:10.0 dotnet publish samples/SliceFx.WasiSample -r wasi-wasm -c Release`. The CopyWasiWasmComponent target copies the generated component to `samples/SliceFx.WasiSample/dist/slice-wasi-sample.wasm`. For Cloudflare Workers: `samples/SliceFx.WasiSample/dist` uses `@bytecodealliance/jco` to transpile the component and `shim.mjs` to bridge Cloudflare's `fetch(Request)` to the wasi:http handler (`npm ci` in the checked-in sample, then `npm run transpile`; scaffolds without a lockfile use `npm install` first). For Fermyon Cloud / Spin: deploy `spin.toml` + `dist/slice-wasi-sample.wasm` directly — Spin natively understands `wasi:http/incoming-handler`. Treat SliceFx.Wasi APIs as experimental and the build/transpile/deploy toolchain as unstable upstream preview tooling.

Features returning `IResult`/`Task<IResult>` are excluded from WASI routes automatically (SLICE020 info diagnostic). Body-binding routes must provide `WasiJsonContext`; routes without it are excluded with SLICE021. WASI DataAnnotations validation is source-generated for supported `Required`, `StringLength`, `MinLength`, `MaxLength`, numeric `Range`, `EmailAddress`, `Url`, and `RegularExpression` rules; routes that need reflection-bound validation are excluded with SLICE022. `[Filter<T>]` endpoint filters are not executed in the WASI path (they require ASP.NET's `IEndpointFilter` pipeline); `ISliceValidator<T>` implementations are discovered and run by generated WASI dispatch.

```csharp
var builder = WasiHost.CreateBuilder();
builder.AddSlice();                       // source-generated route wiring
builder.Services.AddSingleton(TimeProvider.System);
var app = builder.Build();
await app.DispatchAsync(request);         // in-process dispatch
```

### SliceFx.Lambda (`src/SliceFx.Lambda/`)

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
See `samples/SliceFx.LambdaSample/` for a complete working example.

### SliceFx.TestHost (`src/SliceFx.TestHost/`)

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
provided by `SliceFx.Testing.ServiceCollectionExtensions`. See
`samples/SliceFx.TestHostSample/` for a runnable demo.

### SliceFx.Cli (`tools/SliceFx.Cli/`)

Local .NET tool (`dotnet-tools.json` at the repo root) exposing the `slicefx` command. Built on `System.CommandLine`. Verbs:

- `slicefx new feature <Name>` / `slicefx new filter <Name>` — scaffolds a feature class or `IEndpointFilter` from `Templates/`.
- `slicefx routes [--format table|json]` — lists every feature in the project plus its **portability classification** (`portable` / `partial` / `aspnet-only`). Reads feature source files directly.
- `slicefx client csharp --output <path>` — generates a typed C# client from the same manifest.

Features returning `IResult`/`Task<IResult>` are classified `aspnet-only` and excluded from WASI routes (matches diagnostic SLICE020).

## Startup pipeline

`src/SliceFx.SourceGenerator/Emit/RegistrationEmitter.cs` emits the registration code. Read it before changing registration behavior.

`AddSlice()` / `MapSlices()` are generated extension methods emitted into the `SliceFx` namespace for host projects. Feature assemblies also emit public non-extension module helpers and assembly markers so host generators can explicitly aggregate referenced feature assemblies without runtime scanning and validate endpoint-name uniqueness across aggregated modules. Aggregation is local-only by default; `SliceReferencedAssemblies` allow-lists referenced assembly simple names, and `SliceAggregateReferences=true` opts into aggregating all directly referenced Slice modules. `AddSlice()` registers every filter referenced by `[Filter<T>]` as a scoped service. `MapSlices()` maps each `[Feature]` class: calls `endpoints.MapMethods(pattern, [method], delegate)`, attaches generated DataAnnotations validation when needed, then each `[Filter<T>]` in declaration order, then sets tag/summary/name.

The source generator emits up to three files per assembly: `{AsmName}_SliceRegistrations.g.cs` (ASP.NET registrations, when `Microsoft.AspNetCore.Http.IResult` is referenced), `{AsmName}_SliceWasiRegistrations.g.cs` (WASI registrations, only when `SliceFx.Wasi.Routing.WasiRouteTable` is referenced), and `{AsmName}_SliceRouteManifest.g.cs` — a `SliceRouteDescriptor` record plus `GetSliceRoutesGenerated()` consumed by tooling. The manifest includes the shared portability vocabulary (`portable`, `partial`, `aspnet-only`) and is emitted regardless of which hosting path is referenced. Emitter source: `src/SliceFx.SourceGenerator/Emit/`.

## Repo layout

```
SliceFx.slnx
global.json               # SDK pin 10.0.300 (rollForward: latestFeature)
Directory.Build.props     # net10.0 TFM, LangVersion=latest, TreatWarningsAsErrors, EnforceCodeStyleInBuild
Directory.Build.targets   # ValidateSliceCorePackageReferences guard (zero-dep enforcement)
.editorconfig             # file-scoped namespaces; CI enforces via dotnet format
src/SliceFx.Core/           # the framework: FeatureAttribute, FilterAttribute,
                          # ISliceValidator<T>, SliceValidationResult
src/SliceFx.SourceGenerator/# Roslyn generator emitting AddSlice/MapSlices into namespace SliceFx
src/SliceFx.Lambda/         # AWS Lambda hosting adapter over Amazon.Lambda.AspNetCoreServer.Hosting
src/SliceFx.TestHost/       # in-process test host wrapper over Microsoft.AspNetCore.Mvc.Testing
src/SliceFx.Wasi/           # WASI / wasi:http adapter (ASP.NET-independent)
                          # WasiHost, WasiApp, WasiRouteTable, SliceResult
tools/SliceFx.Cli/          # .NET tool: slicefx new feature|filter, slicefx routes, slicefx client csharp
tests/                    # xUnit: SliceFx.Core.Tests, SliceFx.SourceGenerator.Tests,
                          # SliceFx.Wasi.Tests, SliceFx.Cli.Tests
docs/                     # published to GitHub Pages via .github/workflows/pages.yml
README.md, CHANGELOG.md, CONTRIBUTING.md, CODE_OF_CONDUCT.md, SECURITY.md, LICENSE
samples/SliceFx.Sample/
  Program.cs              # bootstrap: AddSlice → MapSlices → Run
  appsettings.json        # port 5099
  Features/<Group>/*.cs   # one feature per file
  Filters/*.cs            # IEndpointFilter implementations
  Services/               # demo IUserStore / InMemoryUserStore
samples/SliceFx.LambdaSample/   # Lambda-ready sample: AddSlice → UseSliceLambda → MapSlices → RunOnLambdaAsync
samples/SliceFx.TestHostSample/ # in-process HTTP demo against SliceFx.Sample
samples/SliceFx.WasiSample/  # WASI sample: WasiHost.CreateBuilder → AddSlice → DispatchAsync
  Features/               # Health (GET /health) and Echo (POST /echo)
  IncomingHandlerImpl.cs  # wasi:http/incoming-handler → WasiApp.DispatchAsync bridge
  spin.toml               # Fermyon Cloud / Spin deployment manifest
  dist/                   # build output + Cloudflare Workers deployment glue
    shim.mjs              # Cloudflare fetch(Request) → wasi:http bridge
    stubs/tcp.js,udp.js   # ABI-level socket stubs (unused by app; Cloudflare has no Node socket APIs)
```

Mixed-language comments are acceptable (the sample contains a Japanese comment in `Program.cs`).
