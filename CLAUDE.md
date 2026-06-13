# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

SliceFx is an experimental .NET web framework built on ASP.NET Core Minimal API that makes Vertical Slice Architecture the primary unit: **1 file = 1 feature = 1 deploy unit**. The runtime framework lives in `src/SliceFx.Core/`; the optional Roslyn source generator lives in `src/SliceFx.SourceGenerator/`. `samples/SliceFx.Sample/` is the reference for how user code should look.

This is pre-1.0 experimental software. Source Generator / AOT, TestHost, Lambda (ASP.NET-hosted and function-per-feature NativeAOT), fluent validation, WASI, and CLI scaffolding are implemented experimentally, but preview packages should use `0.x` versions until the public API is intentionally stabilized. WASI publishing also depends on unstable upstream preview tooling; do not present componentize-dotnet, NativeAOT-LLVM, `jco`, `preview2-shim`, Wrangler, or Cloudflare runtime behavior as SliceFx-controlled guarantees. CI (`.github/workflows/ci.yml`) runs restore, Release build, tests, package verification, a `dotnet format SliceFx.slnx --verify-no-changes --severity info --exclude-diagnostics CS1591` gate, and a guard that fails if `src/SliceFx.Core/SliceFx.Core.csproj` gains a `<PackageReference>`. Other workflows: `pages.yml` publishes `docs/` to GitHub Pages; `perf.yml` runs nightly source-generator + runtime benchmarks and commits chart updates; `analyzer-canary.yml` reports drift against the latest .NET 10 analyzer set monthly without breaking the pinned baseline; `lambda-nativeaot-arm64.yml` packages the linux-arm64 Lambda NativeAOT fixture on a self-hosted ARM64 runner; `wasi-wasm-publish.yml` publishes the WasiSample on ubuntu-latest (weekly + WASI-related PR paths) to gate `IncomingHandlerImpl.cs` compilation under wasi-wasm. Targets **.NET 10** (`Directory.Build.props` pins `<TargetFramework>net10.0</TargetFramework>` with `LangVersion=latest`; the source generator stays on `netstandard2.0` as Roslyn requires). SDK pinned via `global.json` to `10.0.300` (`rollForward: latestFeature`).

## First-time setup

After cloning, activate the pre-push format gate (mirrors CI):

```bash
git config core.hooksPath .githooks
```

## Commands

```bash
dotnet build
dotnet test SliceFx.slnx --configuration Release --no-build --no-restore
dotnet run --project samples/SliceFx.Sample        # listens on http://localhost:5099
dotnet run --project samples/SliceFx.LambdaSample  # listens on http://localhost:5100 (Lambda-ready)
dotnet run --project samples/SliceFx.LambdaFunctionPerFeatureSample  # http://localhost:5000 (default); demo for function-per-feature NativeAOT packaging
dotnet run --project samples/SliceFx.TestHostSample # in-process HTTP demo (no server port)
dotnet run --project samples/SliceFx.BlazorSample/SliceFx.BlazorSample.Server  # API on http://localhost:5101 (run alongside the Client)
dotnet run --project samples/SliceFx.BlazorSample/SliceFx.BlazorSample.Client  # WASM dev server on http://localhost:5102
dotnet publish samples/SliceFx.WasiSample -r wasi-wasm -c Release  # Linux x64 / Windows x64, or Docker linux/amd64 on macOS
dotnet run --project samples/SliceFx.AotSample        # listens on http://localhost:5103 (JIT dev run)
dotnet publish samples/SliceFx.AotSample -c Release    # macOS: osx-arm64 native binary; Linux: add -r linux-x64
dotnet format SliceFx.slnx --verify-no-changes --severity info --exclude-diagnostics CS1591 xUnit1004  # matches CI; drop --verify-no-changes to apply
```

xUnit tests live under `tests/` — runtime tests (`SliceFx.Core.Tests`, `SliceFx.SourceGenerator.Tests`, `SliceFx.TestHost.Tests`, `SliceFx.AotSample.Tests`, `SliceFx.Wasi.Tests`, `SliceFx.Lambda.Tests`, `SliceFx.Lambda.FunctionPerFeature.Tests`, `SliceFx.Cli.Tests`), the `SliceFx.Lambda.NativeAotFixture` support project, and benchmarks (`SliceFx.Benchmarks`, `SliceFx.Benchmarks.Runtime`, plus the `SliceFx.Benchmarks.RuntimeApps/Bench{50,100,200}` size-graded scenario apps). CI runs `dotnet test SliceFx.slnx` (whole solution); use `dotnet test tests/<Name>` to target one project, or `dotnet test SliceFx.slnx --filter "FullyQualifiedName~<Substring>"` for a single test. Also smoke-test the main sample when behavior changes (app must be running):

```bash
curl http://localhost:5099/health
curl -X POST http://localhost:5099/users -H "Content-Type: application/json" \
  -d '{"name":"Alice","email":"alice@example.com"}'
curl -X DELETE http://localhost:5099/users/{id} -H "X-API-Key: secret"
```

## Hard constraints

- **SliceFx.Core stays zero-dependency.** `src/SliceFx.Core/SliceFx.Core.csproj` must never gain a `<PackageReference>`. Enforced in two places: an MSBuild target `ValidateSliceCorePackageReferences` in `Directory.Build.targets` that fails the build locally, and a PowerShell step in `.github/workflows/ci.yml` that fails CI. The only allowed reference is `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. Satellite projects (`src/SliceFx.SourceGenerator`, `src/SliceFx.Lambda`, `src/SliceFx.Lambda.FunctionPerFeature`, `src/SliceFx.TestHost`, `src/SliceFx.Wasi`, `tools/SliceFx.Cli`, and their samples) restore from nuget.org normally. `src/Shared/SliceRouteManifestSchema.cs` is `<Compile Include>`-linked into the source generator and the CLI — keep both copies of the schema in sync via that single file.
- **No new framework abstractions.** SliceFx intentionally avoids `IPipelineBehavior`, `IMediator`, etc. Cross-cutting concerns reuse ASP.NET Core's `IEndpointFilter`.
- **No per-request reflection.** Anything that runs per-request must avoid reflection. The source generator emits `AddSlice` / `MapSlices` — the sole registration path — which avoids startup reflection entirely (AOT-friendly).
- **`WebApplication.CreateSlimBuilder` is intentional** (trimming-friendly host). Do not switch to `CreateBuilder` without reason.
- **Experimental satellite scope.** Source generator, AWS Lambda adapter (ASP.NET-hosted), `SliceFx.Lambda.FunctionPerFeature` (per-feature NativeAOT custom-runtime binaries packaged via the CLI), TestHost, fluent validator (`ISliceValidator<T>`), CLI scaffolding, and Cloudflare WASI adapter (`SliceFx.Wasi` package) are all implemented experimentally. `SliceFx.Wasi` provides in-process dispatch and a componentize-dotnet WASI publish path targeting `wasi:http/incoming-handler@0.2.0` — deployable to Cloudflare Workers (via jco) and Fermyon Cloud / Spin (natively). Do not use the `wasi-experimental` Mono workload for this codebase.
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
- Filters are plain `IEndpointFilter` classes under `Filters/`. `AddSlice()` discovers every `[Filter<T>]` reference and registers `T` as **scoped** in DI. Generated DataAnnotations validation is always attached before any `[Filter<T>]`. Filter ordering can be expressed declaratively via `[FilterOrderHint(After = typeof(OtherFilter))]`; the source generator reports SLICE007 when a feature's declared `[Filter<T>]` order contradicts the hint.
- Full diagnostic catalog (IDs, severities, titles) lives in `src/SliceFx.SourceGenerator/AnalyzerReleases.Unshipped.md`. Notable ranges: SLICE00x handler/route shape, SLICE01x ASP.NET validation, SLICE02x WASI route eligibility, SLICE03x Lambda function-per-feature eligibility, SLICE04x `[SliceJsonContext]` overrides, SLICE05x cross-assembly aggregation, SLICE06x raw Minimal API overlap detection, SLICE07x ASP.NET NativeAOT-safe registration (triggered by `[assembly: SliceAspNetAot]`).

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

WASI features should return `SliceResult<T>`, `SliceResult`, a POCO, `Task<T>`, or `ValueTask<T>`. `WasiResponse` is also accepted as a raw escape hatch for streaming or custom serialization. Features returning `IResult`/`Task<IResult>` are excluded from WASI routes automatically (SLICE020 info diagnostic). Body-binding routes must provide `WasiJsonContext`; routes without it are excluded with SLICE021 — for `SliceResult<T>`, the JSON context needs to register `T` (the payload type), not the wrapper struct.

**Host-neutral typed result** (preview.7+): `SliceResult<T>` (`SliceFx.Core`, namespace `SliceFx`) lets a feature express a typed success body **and** an error path from a single `Handle` return type, without giving up typed-client generation. The source generator detects this type, registers `T` as the JSON root (not the wrapper), and emits `result.ToWasiResponse(__JsonTypeInfo<T>())`. `SliceResult` (non-generic) handles status-only, non-JSON, and redirect results; the generated C# client emits `Task` (void) for these routes. `SliceResult<T>` factory methods: `Ok(T value)`, `Created(T value, string location)`, `NoContent()`, `NotFound(string? detail)`, `Unauthorized(string? detail)`, `BadRequest(string? detail)`, `Problem(int status, string title, string? detail)`. `SliceResult` (non-generic) factory methods: `Ok()`, `NoContent()`, `Created(string location)`, `NotFound(string? detail)`, `Unauthorized(string? detail)`, `BadRequest(string? detail)`, `Problem(int status, string title, string? detail)`, `Redirect(string location, bool permanent = false)`, `Html(string html)`, `Text(string text)`, `Content(string body, string contentType, int status = 200)`, `Bytes(byte[] body, string contentType, int status = 200)`. The `Redirect/Html/Text/Content/Bytes` factories set `SliceResultKind.Redirect` or `SliceResultKind.RawBody`; the source generator emits the appropriate host-specific response (ASP.NET: generated `__SliceResultToIResult` wrapper; WASI: `ToWasiResponse()`; Lambda: `ToLambdaResponse()`). The generated C# client treats non-generic `SliceResult` as `Task` (void); raw bodies like Html/Bytes are not surfaced in the client (documented limitation).

```csharp
// Typed body + error path — client generates Task<GetItemResponse> GetItemAsync(string id)
[Feature("GET /items/{id}")]
public static class GetItem
{
    public static async Task<SliceResult<GetItemResponse>> Handle(string id, IKeyValueStore kv, CancellationToken ct)
    {
        var item = await kv.GetJsonAsync($"item:{id}", InboxJsonContext.Default.InboxItem, ct);
        if (item is null) return SliceResult<GetItemResponse>.NotFound($"Item '{id}' not found.");
        return SliceResult<GetItemResponse>.Ok(new GetItemResponse(item));
    }
}

// Status-only (204) — client generates Task DeleteItemAsync(string id)
[Feature("DELETE /items/{id}")]
public static class DeleteItem
{
    public static async Task<SliceResult> Handle(string id, IKeyValueStore kv, CancellationToken ct)
    {
        if (!await kv.ExistsAsync($"item:{id}", ct)) return SliceResult.NotFound();
        await kv.DeleteAsync($"item:{id}", ct);
        return SliceResult.NoContent();
    }
}
```

WASI DataAnnotations validation is source-generated for supported `Required`, `StringLength`, `MinLength`, `MaxLength`, numeric `Range`, `EmailAddress`, `Url`, `HttpsUrl`, and `RegularExpression` rules (shape-conditional: `StringLength` for `string` only, `Range` for numeric types only, and any supported attribute with a resource/localized error message is treated as unsupported); routes that need reflection-bound validation — including `IValidatableObject`, type-level attributes, custom `ValidationAttribute`, and supported attribute types used in unsupported shapes — are excluded from the WASI route table with SLICE022. There is no per-request reflection fallback in the WASI path. `[Filter<T>]` endpoint filters are not executed in the WASI path (they require ASP.NET's `IEndpointFilter` pipeline); `ISliceValidator<T>` implementations are discovered and run by generated WASI dispatch.

```csharp
var builder = WasiHost.CreateBuilder();
builder.AddSlice();                       // source-generated route wiring
builder.Services.AddSingleton(TimeProvider.System);
var app = builder.Build();
await app.DispatchAsync(request);         // in-process dispatch
```

**wasi:http marshalling helpers** (`SliceFx.Wasi`, namespace `SliceFx.Wasi`, class `WasiHttpMarshalling`): pure-logic utilities for bridging WIT-bound types to `WasiRequest`/`WasiResponse`. All methods operate on primitive types with no WIT dependency; call sites cross the WIT boundary once (to get raw bytes/entries), then delegate here. Methods: `SplitPathAndQuery(string, out string, out string?)` — splits `IncomingRequest.PathWithQuery()` into path and query; `ParseHeaders(IEnumerable<(string, byte[])>)` — decodes wasi:http header entries to a case-insensitive `Dictionary<string, string>` (non-UTF-8 values fall back to Latin-1); `IsBodySizeWithinLimit(IReadOnlyDictionary<string, string>, int)` — validates declared `Content-Length` against a max-bytes limit (returns `true` when header is absent); `FormatResponseHeaders(IReadOnlyDictionary<string, string>)` — encodes response headers as lowercase-name UTF-8-value pairs for `Fields.FromList`.

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

### SliceFx.Lambda.FunctionPerFeature (`src/SliceFx.Lambda.FunctionPerFeature/`)

Experimental NativeAOT custom-runtime packaging that compiles **one Lambda artifact per eligible feature** (API Gateway HTTP API v2). Opt in at assembly scope with `[assembly: LambdaFunctionPerFeature]`; per-feature DI is configured by an `ILambdaFunctionPerFeatureStartup` discovered through `[LambdaFunctionStartup(typeof(MyStartup))]`. Features remain ordinary `[Feature]` static classes — the same source-generator pass that emits `MapSlices` also emits per-feature handler entry points and feeds the manifest the CLI uses to pack each artifact.

Eligibility is enforced by SLICE03x diagnostics: features returning `IResult` (SLICE030), with `[Filter<T>]` filters (SLICE031), needing reflection-bound DataAnnotations (SLICE034), or with unsupported parameter shapes / keyed-service keys (SLICE033, SLICE037) are skipped; body-binding handlers must provide a Lambda JSON context to avoid SLICE032, and duplicate startup types (SLICE035) or artifact IDs (SLICE036) are build errors. The same Program.cs runs locally on Kestrel via `UseSliceLambda()` and `RunOnLambdaAsync()` for iterative development — function-per-feature packaging is invoked through the CLI:

```bash
slicefx manifest aws-lambda --output template.yaml      # SAM template for the eligible features
slicefx package aws-lambda --mode function-per-feature --rid linux-x64
```

See `samples/SliceFx.LambdaFunctionPerFeatureSample/` for the full Program.cs + per-feature startup shape (default Kestrel port 5000). The `tests/SliceFx.Lambda.NativeAotFixture` project is a build-time fixture used by NativeAOT packaging tests, not a runnable app.

### SliceFx.Wasi.Spin (`src/SliceFx.Wasi.Spin/`)

Spin-specific WASI satellite (experimental). Provides `ISpinCronHandler` / `SpinCronDispatcher` for Spin cron triggers and `ISpinVariables` for reading Spin application variables. Both are registered via `WasiHostBuilderSpinExtensions` — pure manual DI, no source-generator involvement.

**Cron triggers:** Implement `ISpinCronHandler` and register it with `builder.AddSpinCronHandler(...)`. From the WIT-bound cron export entry point, call `await SpinCronDispatcher.DispatchAsync(app, context, ct)`. `SpinCronContext` carries only `FireTime` (`spin:cron@3.0.0` WIT has no metadata field). Use `RecordingSpinCronHandler` in tests. Cron expressions must be **6 fields** `{sec} {min} {hour} {dom} {month} {dow}` — 7-field expressions cause a `ParseSchedule` error at runtime. `async func` export encoding failures were fixed in componentize-dotnet 0.8.0 / wit-bindgen 0.58; however, C# async-export codegen remains preview quality (generated code contains `// TODO` and unimplemented `future`/`stream` paths). SliceFx therefore keeps sync `func` + `.GetAwaiter().GetResult()` as the recommended default; declare the WIT cron export as `func` (sync) and call the async handler synchronously (`.GetAwaiter().GetResult()`) inside the entry point. The world-level `handle-cron-event` export is wired differently from `wasi:http/incoming-handler` — it uses the `IProxyWorld` pattern, not `IIncomingHandlerExports`.

**Spin variables:** Implement `ISpinVariables` and register it with `builder.AddSpinVariables(...)`. The interface surface is async (`ValueTask<string?> GetAsync(string name, CancellationToken ct = default)`), matching the repo's convention of async-surface over synchronous WIT host calls. On Fermyon Cloud / Spin, implement using the WIT-generated free function — componentize-dotnet emits it on a `*ImportsInterop` static class (e.g. `VariablesImportsInterop.Get` for `fermyon:spin/variables@2.0.0`); the generated `IVariablesImports` type carries only the `Error` shape. Wrap the synchronous call with `ValueTask.FromResult(...)`. Implementations should be fail-closed: undefined or unresolvable → `null`. Use `InMemorySpinVariables` in tests.

**WASI constraints relevant to Spin implementations:** `System.Security.Cryptography` is unavailable in NativeAOT-LLVM WASI builds (including `CryptographicOperations.FixedTimeEquals`); use a manual XOR-accumulation loop for constant-time comparison.

```csharp
var builder = WasiHost.CreateBuilder();
builder.AddSpinCronHandler<MyCronHandler>();     // cron trigger handler
builder.AddSpinVariables<MySpinVariablesImpl>(); // Spin variables (fail-closed)
var app = builder.Build();
// In the WIT cron export entry point:
SpinCronDispatcher.DispatchAsync(app, context, ct).GetAwaiter().GetResult();
```

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

- `slicefx new feature <Name>` / `slicefx new filter <Name>` / `slicefx new wasi-cloudflare` — scaffolds a feature class, `IEndpointFilter`, or a Cloudflare Workers wasi:http deployment skeleton from `Templates/`.
- `slicefx routes [--format table|json]` — lists every feature in the project plus its **portability classification** (`portable` / `partial` / `aspnet-only`). Prefers the source-generated route manifest; falls back to scanning `Features/**/*.cs` if the project hasn't been built.
- `slicefx client csharp --output <path>` / `slicefx client typescript --output <path>` — typed clients from the same manifest. The C# client reuses feature request/response types; the TypeScript client emits interfaces and is type-checked in CI via `eng/typescript-typecheck/` (pinned local `tsc`).
- `slicefx manifest aws-lambda` — emits an AWS SAM template for eligible features.
- `slicefx package aws-lambda --mode function-per-feature --rid <linux-x64|linux-arm64>` — drives the per-feature NativeAOT publish/pack pipeline used by `SliceFx.Lambda.FunctionPerFeature`.
- `slicefx openapi` — emits an OpenAPI document from the manifest.

Features returning `IResult`/`Task<IResult>` are classified `aspnet-only` and excluded from WASI routes (matches diagnostic SLICE020). The function-per-feature Lambda eligibility filter uses the SLICE03x diagnostics described above.

## Startup pipeline

`src/SliceFx.SourceGenerator/Emit/RegistrationEmitter.cs` emits the registration code. Read it before changing registration behavior.

`AddSlice()` / `MapSlices()` are generated extension methods emitted into the `SliceFx` namespace for host projects. Feature assemblies also emit public non-extension module helpers and assembly markers so host generators can explicitly aggregate referenced feature assemblies without runtime scanning and validate endpoint-name uniqueness across aggregated modules. Aggregation is local-only by default; the MSBuild properties `SliceFxReferencedAssemblies` (allow-list of referenced assembly simple names) and `SliceFxAggregateReferences=true` (aggregate all directly referenced Slice modules) opt into cross-assembly aggregation — referenced Slice modules that aren't covered fire SLICE050 / SLICE051. `AddSlice()` registers every filter referenced by `[Filter<T>]` as a scoped service. `MapSlices()` maps each `[Feature]` class: calls `endpoints.MapMethods(pattern, [method], delegate)`, attaches generated DataAnnotations validation when needed, then each `[Filter<T>]` in declaration order, then sets tag/summary/name. Raw `app.MapGet/MapPost/...` calls that collide with a Slice route or endpoint name are detected by SLICE060 / SLICE061.

The source generator emits up to four files per assembly: `{AsmName}_SliceRegistrations.g.cs` (ASP.NET registrations, when `Microsoft.AspNetCore.Http.IResult` is referenced), `{AsmName}_SliceWasiRegistrations.g.cs` (WASI registrations, only when `SliceFx.Wasi.Routing.WasiRouteTable` is referenced), `{AsmName}_SliceLambdaFunctionPerFeature.g.cs` (NativeAOT per-feature Lambda entry points, when `SliceFx.Lambda.FunctionPerFeature` is referenced and `[assembly: LambdaFunctionPerFeature]` is set), and `{AsmName}_SliceRouteManifest.g.cs` — a `SliceRouteDescriptor` record plus `GetSliceRoutesGenerated()` consumed by tooling. The manifest includes the shared portability vocabulary (`portable`, `partial`, `aspnet-only`) and is emitted regardless of which hosting path is referenced. Emitter source: `src/SliceFx.SourceGenerator/Emit/`; the manifest schema record lives in `src/Shared/SliceRouteManifestSchema.cs` and is `<Compile Include>`-linked into both the generator and the CLI.

## Repo layout

```
SliceFx.slnx
global.json               # SDK pin 10.0.300 (rollForward: latestFeature)
Directory.Build.props     # net10.0 TFM, LangVersion=latest, TreatWarningsAsErrors, EnforceCodeStyleInBuild
Directory.Build.targets   # ValidateSliceCorePackageReferences guard (zero-dep enforcement)
.editorconfig             # file-scoped namespaces; CI enforces via dotnet format
.node-version             # Node version pinned for the TypeScript client type-check test
dotnet-tools.json         # slicefx CLI as a local .NET tool
src/Shared/SliceRouteManifestSchema.cs  # manifest record shared (linked) between source-gen and CLI
src/SliceFx.Core/           # the framework: FeatureAttribute, FilterAttribute,
                          # ISliceValidator<T>, SliceValidationResult
src/SliceFx.SourceGenerator/# Roslyn generator emitting AddSlice/MapSlices into namespace SliceFx
src/SliceFx.Lambda/         # AWS Lambda hosting adapter over Amazon.Lambda.AspNetCoreServer.Hosting
src/SliceFx.Lambda.FunctionPerFeature/ # per-feature NativeAOT Lambda packaging (custom runtime)
src/SliceFx.TestHost/       # in-process test host wrapper over Microsoft.AspNetCore.Mvc.Testing
src/SliceFx.Wasi/           # WASI / wasi:http adapter (ASP.NET-independent)
                          # WasiHost, WasiApp, WasiRouteTable, WasiResponse, WasiResults
tools/SliceFx.Cli/          # .NET tool: slicefx new|routes|client|manifest|package|openapi
tools/gen-bench-features.py # generator for the Bench50/100/200 benchmark scenario apps
tests/                    # xUnit runtime tests + benchmarks + NativeAOT fixture:
                          #   SliceFx.Core.Tests, SliceFx.SourceGenerator.Tests,
                          #   SliceFx.TestHost.Tests, SliceFx.Wasi.Tests,
                          #   SliceFx.Lambda.Tests, SliceFx.Lambda.FunctionPerFeature.Tests,
                          #   SliceFx.Cli.Tests, SliceFx.Lambda.NativeAotFixture,
                          #   SliceFx.Benchmarks (source-gen), SliceFx.Benchmarks.Runtime,
                          #   SliceFx.Benchmarks.RuntimeApps/Bench{50,100,200}
eng/typescript-typecheck/ # locally pinned tsc workspace used by the generated TS client type-check test
docs/                     # published to GitHub Pages via .github/workflows/pages.yml
.github/workflows/        # ci.yml, pages.yml, perf.yml (nightly bench), analyzer-canary.yml (monthly drift),
                          # lambda-nativeaot-arm64.yml (weekly arm64 NativeAOT fixture)
README.md, CHANGELOG.md, CONTRIBUTING.md, CODE_OF_CONDUCT.md, SECURITY.md, LICENSE, ONBOARDING.md
samples/SliceFx.Sample/
  Program.cs              # bootstrap: AddSlice → MapSlices → Run
  appsettings.json        # port 5099
  Features/<Group>/*.cs   # one feature per file
  Filters/*.cs            # IEndpointFilter implementations
  Services/               # demo IUserStore / InMemoryUserStore
samples/SliceFx.LambdaSample/   # ASP.NET-hosted Lambda sample: AddSlice → UseSliceLambda → MapSlices → RunOnLambdaAsync (port 5100)
samples/SliceFx.LambdaFunctionPerFeatureSample/ # per-feature NativeAOT packaging demo (Kestrel locally on port 5000)
  LambdaSetup.cs          # [assembly: LambdaFunctionPerFeature] opt-in
  Features/Orders/        # incl. OrderFeatureStartup.cs (ILambdaFunctionPerFeatureStartup)
samples/SliceFx.AotSample/    # NativeAOT sample: [assembly: SliceAspNetAot] + AOT-safe dispatch (port 5103)
  AotSetup.cs               # [assembly: SliceAspNetAot] opt-in
  AotJsonContext.cs         # [SliceJsonContext(SliceJsonTarget.AspNet)] for body/response serialization
  Dockerfile                # multi-stage: sdk:10.0 + clang/zlib1g-dev → runtime-deps:10.0-noble-chiseled
  README.md                 # publish/container/limitation guide
samples/SliceFx.TestHostSample/ # in-process HTTP demo against SliceFx.Sample
samples/SliceFx.WasiSample/  # WASI sample: WasiHost.CreateBuilder → AddSlice → DispatchAsync
  IncomingHandlerImpl.cs  # wasi:http/incoming-handler → WasiApp.DispatchAsync bridge
  WasiJsonContext.cs      # source-gen JsonSerializerContext for body-binding routes
  spin.toml               # Fermyon Cloud / Spin deployment manifest
  wit/                    # WIT interface imports
  dist/                   # build output + Cloudflare Workers deployment glue
    shim.mjs              # Cloudflare fetch(Request) → wasi:http bridge
    stubs/tcp.js,udp.js   # ABI-level socket stubs (unused by app; Cloudflare has no Node socket APIs)
samples/SliceFx.BlazorSample/
  SliceFx.BlazorSample.Server/  # SliceFx API (port 5101): AddSlice → MapSlices → CORS for Client
  SliceFx.BlazorSample.Client/  # Blazor WASM dev server (port 5102): SliceApiClient.g.cs generated by slicefx CLI
  SliceFx.BlazorSample.Contracts/ # shared request/response records for EditForm two-way binding
```

Mixed-language comments are acceptable (the sample contains a Japanese comment in `Program.cs`).
