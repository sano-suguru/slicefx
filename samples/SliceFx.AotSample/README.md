# SliceFx.AotSample

Demonstrates SliceFx NativeAOT-safe dispatch — the same Todo feature file runs as a
standalone native binary with no JIT and a cold start in milliseconds.

**Status:** Experimental. Requires `[assembly: SliceAspNetAot]` and a
`[SliceJsonContext(SliceJsonTarget.AspNet)]` context. See [docs/aot.md](../../docs/aot.md).

## Features

| Route | Feature class | Notes |
|---|---|---|
| `GET /health` | `Health.GetHealth` | Health check; CI smoke target |
| `POST /todos` | `Todos.CreateTodo` | JSON body + DataAnnotations validation |
| `GET /todos/{id:guid}` | `Todos.GetTodo` | Route param + `SliceResult<T>` 404 |
| `GET /todos` | `Todos.ListTodos` | Collection response |
| `DELETE /todos/{id:guid}` | `Todos.DeleteTodo` | void/`Task` handler → 200 (RDF parity) |

## Requirements

| Platform | Pre-requisites |
|---|---|
| **macOS** | Xcode CLT (`xcode-select --install`), .NET 10 SDK 10.0.300+ |
| **Linux** | `clang`, `zlib1g-dev`, .NET 10 SDK 10.0.300+ |
| **Docker (any OS)** | Docker; produces a linux/amd64 image |

## Publish and run (native binary)

### macOS (osx-arm64 or osx-x64 — native architecture)

```bash
# From repository root
dotnet publish samples/SliceFx.AotSample -c Release

# Binary: samples/SliceFx.AotSample/bin/Release/net10.0/osx-arm64/publish/SliceFx.AotSample
./samples/SliceFx.AotSample/bin/Release/net10.0/osx-arm64/publish/SliceFx.AotSample
```

### Linux x64

```bash
dotnet publish samples/SliceFx.AotSample -c Release -r linux-x64

# Binary: samples/SliceFx.AotSample/bin/Release/net10.0/linux-x64/publish/SliceFx.AotSample
ASPNETCORE_URLS=http://localhost:5103 \
  ./samples/SliceFx.AotSample/bin/Release/net10.0/linux-x64/publish/SliceFx.AotSample
```

To produce a Linux binary from macOS without Docker, use `--platform linux/amd64` in
the Docker build below; cross-compiling NativeAOT without a native cross-toolchain is
not supported.

### Expected binary size

| Configuration | Approximate size |
|---|---|
| Binary (stripped) | 12–20 MB |
| Container image (binary + runtime-deps) | 25–35 MB |

`InvariantGlobalization=true` and `StripSymbols=true` are already set in the project.
Use `<OptimizationPreference>Size</OptimizationPreference>` to reduce further.

## Container build and run

The `Dockerfile` uses a multi-stage build (SDK → distroless runtime-deps). Build
context must be the repository root because the sample uses `ProjectReference` into `src/`:

```bash
# Standard build (matches the host platform — linux/amd64 on Linux CI)
docker build -f samples/SliceFx.AotSample/Dockerfile -t slicefx-aot .

# Force linux/amd64 from Apple Silicon
docker build --platform linux/amd64 \
  -f samples/SliceFx.AotSample/Dockerfile \
  -t slicefx-aot .

# Run
docker run --rm -p 8080:8080 slicefx-aot
```

### Verify

```bash
# Health
curl http://localhost:8080/health
# {"status":"ok","at":"..."}

# Create todo
curl -X POST http://localhost:8080/todos \
  -H 'Content-Type: application/json' \
  -d '{"title":"native todo"}'
# {"id":"...","title":"native todo","createdAt":"..."}

# DataAnnotations validation (400)
curl -s -w "\nstatus=%{http_code}" \
  -X POST http://localhost:8080/todos \
  -H 'Content-Type: application/json' \
  -d '{"title":""}'

# SliceResult<T> not found (404)
curl -s -w "\nstatus=%{http_code}" \
  http://localhost:8080/todos/00000000-0000-0000-0000-000000000001
```

## AOT mode limitations

| Limitation | Detail |
|---|---|
| **No Accepts/Produces inference** | `RequestDelegate` endpoints do not emit parameter-type-based metadata. Use `slicefx openapi` instead of `AddOpenApi()` for an accurate OpenAPI document. |
| **Reflection-dependent DataAnnotations** | `IValidatableObject`, type-level, or custom `ValidationAttribute` subclasses produce SLICE072 and must be moved to `ISliceValidator<T>`. Supported: `Required`, `StringLength`, `MinLength`, `MaxLength`, `EmailAddress`, `Url`, `HttpsUrl`, `RegularExpression`, `Range`. |
| **Unsupported parameter shapes** | `IFormFile`, `[AsParameters]`, multi-value query, and `BindAsync` custom types produce SLICE070. |
| **JSON context required** | Every request body and response type must appear in the `[SliceJsonContext(SliceJsonTarget.AspNet)]` context (SLICE071 if missing). |
| **Group-level filters** | `MapGroup(…).AddEndpointFilter(…)` wraps the `RequestDelegate` with empty `Arguments`. Feature-declared `[SliceFilter<T>]` and `[Filter<T>]` work correctly. |

See [docs/aot.md](../../docs/aot.md) for the full guide and diagnostic reference.
