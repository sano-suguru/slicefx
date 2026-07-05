[日本語](ja/aot.md)

# NativeAOT — Native Binary Deployment

SliceFx features are compiled to standard Minimal API `MapMethods` registrations.
Because the source generator emits all binding and serialization code at compile time
and avoids startup-time reflection, the generated code can be published as a standalone
native binary using .NET NativeAOT — with no JIT, no runtime-deps bundle, and a cold
start measured in milliseconds.

## Deployment target comparison

| Target | How | When to choose |
|---|---|---|
| **Plain NativeAOT container** | `dotnet publish -r <rid>` + distroless image | General-purpose lightweight server, Azure Container Apps, Fly.io, Kubernetes |
| **Lambda function-per-feature** | `slicefx package aws-lambda --mode function-per-feature` | Per-feature cold-start isolation on AWS Lambda ([docs/lambda.md](lambda.md)) |
| **WASI component** | `dotnet publish -r wasi-wasm` | Cloudflare Workers, Fermyon Spin ([samples/SliceFx.WasiSample](../samples/SliceFx.WasiSample/README.md)) |

Plain NativeAOT is the most broadly supported path and the only one backed by
fully stable upstream tooling. Lambda function-per-feature and WASI are experimental
and depend on preview packages.

## Enabling AOT-safe dispatch

By default, SliceFx registers features through `RequestDelegateFactory` (RDF), which
uses runtime reflection and is not AOT-compatible. Add `[assembly: SliceAspNetAot]` to
opt into the generated AOT-safe registration mode:

```csharp
// AotSetup.cs
[assembly: SliceAspNetAot]
```

The source generator detects this attribute and emits `new RequestDelegate(…)` handlers
that do all parameter binding, validation, and JSON serialization using
`System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>` — no per-request reflection.

### JSON context

You must provide a `JsonSerializerContext` that covers every request body and response
type used by your features. Mark it with `[SliceJsonContext(SliceJsonTarget.AspNet)]`:

```csharp
// AotJsonContext.cs
[SliceJsonContext(SliceJsonTarget.AspNet)]
[JsonSerializable(typeof(CreateTodo.Request))]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(List<Todo>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AotJsonContext : JsonSerializerContext { }
```

The generator uses the `[JsonSerializable]` roots to distinguish body parameters from
DI services at compile time (the same mechanism used by the WASI and Lambda paths).

### Supported parameter shapes

| Shape | Binding |
|---|---|
| Route param (`{id}`) | `SliceAotArgumentBinder.TryGetFromRoute<T>` |
| Query string (`?page=1`) | `SliceAotArgumentBinder.BindFromQuery<T>` |
| Header | `SliceAotArgumentBinder.BindFromHeader<T>` |
| JSON body | `ReadFromJsonAsync(JsonTypeInfo<T>)` — requires context root |
| DI service | `RequestServices.GetRequiredService(typeof(T))` |
| Keyed service (`[FromKeyedServices]`) | `GetRequiredKeyedService` |
| `CancellationToken` | `HttpContext.RequestAborted` |
| `HttpContext` / `HttpRequest` / `HttpResponse` | Direct pass-through |
| `ClaimsPrincipal` | `HttpContext.User` |

Unsupported shapes (`IFormFile`, `[AsParameters]`, multi-value query, `BindAsync`
custom types) produce a **SLICE070 error** at build time.

### Diagnostics

| ID | Meaning |
|---|---|
| SLICE070 | Parameter type cannot be bound in AOT mode — fix or remove `[assembly: SliceAspNetAot]` |
| SLICE071 | Missing `[JsonSerializable]` root(s) in the `[SliceJsonContext(AspNet)]` context |
| SLICE072 | Reflection-dependent DataAnnotations — use generated-supported attributes or `ISliceValidator<T>` |
| SLICE073 | `IResult` return: the result's own `ExecuteAsync` is called directly; ensure it is AOT-safe |
| SLICE074 | Referenced Slice module was not compiled with `[assembly: SliceAspNetAot]` |

See [docs/source-generator.md](source-generator.md) for the full diagnostic catalog.

**SLICE071 — per-type detection.** When a `[SliceJsonContext(AspNet)]` class exists and already
has at least one `[JsonSerializable]` entry, the generator also checks that every required root
(response type and explicit `[FromBody]` parameter, plus nested `Request` records by convention)
is registered. Missing roots appear as individual type names in the error message, making it
possible to fix the context without guesswork.

When the context exists but has zero `[JsonSerializable]` entries, SLICE071 fires only for the
context-missing case (all roots listed) because an empty context could be intentional.

### Detection scope and limitations

The generator can detect the following roots automatically:

- **Response types** — all targets.
- **Explicit `[FromBody]` parameters** — all targets.
- **Nested `Request` records** (type starts with `FeatureClass.`) — POST/PUT/PATCH convention body; detected without registering the type in the DI container.
- **Sole JSON-context-registered candidate** — on POST/PUT/PATCH, when no nested `Request` type
  is present, the single remaining request-like parameter that is already registered in the
  `[SliceJsonContext(AspNet)]` context is selected as the body (see
  [docs/guides/parameter-binding.md#body-selection-compile-time-paths](guides/parameter-binding.md#body-selection-compile-time-paths)).

The following cannot be detected at compile time:

- **Unannotated complex body parameters** that are neither a nested type of the feature class nor
  already registered in the JSON context — the compiler cannot distinguish these from DI
  services without explicit `[FromBody]`.

### `slicefx json-context`

The CLI provides a workflow companion for the generator diagnostics:

```bash
# Report missing entries (non-zero exit on any gap — suitable for CI)
slicefx json-context --check [--target aspnet|wasi|all] [--project path/to/app.csproj]

# Insert the missing [JsonSerializable] entries in-place
slicefx json-context --fix [--target aspnet|wasi|all] [--project path/to/app.csproj]
```

`--check` and `--fix` can be combined: `--check --fix` reports and patches in a single pass.
When no flag is specified, `--check` is implied.

**Note:** The CLI operates on the source-generated route manifest when the project has been
built; otherwise it falls back to scanning `Features/**/*.cs`. In fallback mode, return
types appear as short names (the text in the source file), so registered FQN entries are
matched by suffix. Build the project first for accurate per-type results.

### Validation

Source-generated DataAnnotations validation (the same rules as the WASI path) is attached
before any `[Filter<T>]` endpoint filters. Reflection-dependent rules — `IValidatableObject`,
type-level attributes, custom `ValidationAttribute` subclasses — produce SLICE072 and must
be moved to `ISliceValidator<T>`.

AOT-safe DataAnnotations attributes: `Required`, `StringLength`, `MinLength`, `MaxLength`,
`EmailAddress`, `Url`, `HttpsUrl`, `RegularExpression`, `Range`.

See [docs/guides/aot-safe-scoped-di.md](guides/aot-safe-scoped-di.md) for DI patterns that
are safe under full trim / NativeAOT.

### Note on `AddOpenApi`

`MapMethods` with a `RequestDelegate` handler returns `IEndpointConventionBuilder`, not
`RouteHandlerBuilder`. Accepts/Produces metadata are not inferred from the parameter types.
If you need an OpenAPI document, use `slicefx openapi` (manifest-based generation) instead
of `AddOpenApi()`.

## Publishing

### macOS (produces a native binary for the host architecture)

```bash
dotnet publish samples/SliceFx.AotSample -c Release
# binary at: samples/SliceFx.AotSample/bin/Release/net10.0/osx-arm64/publish/SliceFx.AotSample
```

### Linux

```bash
dotnet publish samples/SliceFx.AotSample -c Release -r linux-x64
# binary at: samples/SliceFx.AotSample/bin/Release/net10.0/linux-x64/publish/SliceFx.AotSample
```

macOS requires Xcode Command Line Tools (`xcode-select --install`). Linux requires
`clang` and `zlib1g-dev`:

```bash
sudo apt-get install clang zlib1g-dev
```

**Binary size** (release, stripped): approximately 12–20 MB depending on the features
included and whether `InvariantGlobalization=true` is set.

**Trimming knobs:** `<OptimizationPreference>Size</OptimizationPreference>` can reduce the
binary further. `InvariantGlobalization` removes ICU data (~1-3 MB). Both are already set
in the `SliceFx.AotSample` project.

## Containers

The sample ships a multi-stage `Dockerfile` that:

1. **Build stage** (`mcr.microsoft.com/dotnet/sdk:10.0`) — installs `clang` + `zlib1g-dev` (not included in the SDK image), then runs `dotnet publish`. Pass `--platform linux/amd64` when building from Apple Silicon.
2. **Runtime stage** (`mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled`) — a minimal Ubuntu Noble distroless image (~13 MB) that runs as non-root by default.

```bash
# Build from repository root
docker build -f samples/SliceFx.AotSample/Dockerfile -t slicefx-aot .

# Force x64 from Apple Silicon
docker build --platform linux/amd64 \
  -f samples/SliceFx.AotSample/Dockerfile \
  -t slicefx-aot .

# Run
docker run --rm -p 8080:8080 slicefx-aot

# Verify
curl http://localhost:8080/health
curl -X POST http://localhost:8080/todos \
  -H 'Content-Type: application/json' \
  -d '{"title":"container test"}'
```

**Total image size** (binary + runtime deps): approximately 25–35 MB.

## CI verification

The `nativeaot-sample` CI job in `.github/workflows/ci.yml` runs on every push and PR:

1. `dotnet publish samples/SliceFx.AotSample -c Release -r linux-x64` — because `TreatWarningsAsErrors=true`, this step fails on any `IL2026`/`IL3050` diagnostic, asserting that the generated dispatch is reflection-free.
2. Smoke tests: `GET /health` (200), `POST /todos` (200), validation 400, `SliceResult<T>` 404.
3. Binary size is reported to the step summary.

TestHost tests in `tests/SliceFx.AotSample.Tests/` run the AOT-mode generated code under JIT
as part of the main test suite and validate handler logic (binding, validation, SliceResult
mapping, status codes). The publish step is the only gate that catches trim/AOT-specific issues.

## Limitations

- **No Accepts/Produces inference**: `RequestDelegate`-registered endpoints do not emit parameter-type-based metadata. Use `slicefx openapi` for an accurate OpenAPI document.
- **Group-level filters**: `MapGroup(…).AddEndpointFilter(…)` wraps the `RequestDelegate` with an empty `Arguments` list; `[SliceFilter<T>]` and `[Filter<T>]` declared on the feature itself compose correctly.
- **body/DI disambiguation**: determined at compile time by precedence — `[FromBody]`, then a
  nested `Request` type of the feature class, then the sole remaining request-like parameter
  registered in the `[SliceJsonContext(AspNet)]` context (POST/PUT/PATCH only); everything else
  resolves from DI. Idiomatic handlers (a nested `Request` plus any number of injected concrete
  services) now compile without annotation — the nested type claims the body slot and the
  services fall through to DI automatically. **SLICE070** fires only when two parameters
  genuinely tie at the same precedence level (e.g. two candidates both registered in the JSON
  context with no nested `Request` to break the tie); its message identifies the second
  candidate and asks you to disambiguate with `[FromBody]`/`[FromServices]`. See
  [docs/guides/parameter-binding.md#body-selection-compile-time-paths](guides/parameter-binding.md#body-selection-compile-time-paths)
  for the full precedence and residual edge cases.
