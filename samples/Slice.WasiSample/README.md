# Slice.WasiSample

This sample shows Slice `[Feature]` classes — unmodified from their ASP.NET counterparts — compiled to a `wasi:http/incoming-handler` WASM component and deployed to **Cloudflare Workers** (via jco) and **Fermyon Cloud** (via Spin, natively). The same feature handles requests whether the host is ASP.NET Core, AWS Lambda, Cloudflare Workers, or Fermyon Cloud.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (SDK 10.0.300 pinned in `global.json`)
- Linux x64 or Windows x64 for native `wasi-wasm` publish with the current NativeAOT-LLVM preview packages, or Docker with `--platform linux/amd64` on macOS
- [Node.js](https://nodejs.org/) 18+ and `npm` — only for the Cloudflare WASI deployment path
- [Spin CLI](https://developer.fermyon.com/spin/v3/install) — only for the Fermyon Cloud deployment path

**Preview dependencies:**

| Dependency | Version |
| --- | --- |
| `BytecodeAlliance.Componentize.DotNet.Wasm.SDK` | `0.7.0-preview00010` |
| `runtime.<host>.microsoft.dotnet.ilcompiler.llvm` | `10.0.0-alpha.1.25162.1` |
| `@bytecodealliance/jco` | `1.19.0` |
| `@bytecodealliance/preview2-shim` | `0.17.9` |
| `binaryen` | `129.0.0` |

## Build the WASM component

On Linux x64 or Windows x64:

```bash
dotnet publish samples/Slice.WasiSample -r wasi-wasm -c Release
# → copies component to samples/Slice.WasiSample/dist/slice-wasi-sample.wasm
```

On macOS, publish through a Linux x64 Docker container:

```bash
docker run --rm --platform linux/amd64 -v "$PWD":/work -w /work \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet publish samples/Slice.WasiSample -r wasi-wasm -c Release
```

## Deploy to Fermyon Cloud (free tier)

Fermyon Cloud natively understands `wasi:http/incoming-handler`, so no JS transpilation is needed.

```bash
# 1. Install Spin CLI and log in (once)
spin plugins install cloud --yes
spin cloud login

# 2. Build the component (see above), then deploy
cd samples/Slice.WasiSample
spin cloud deploy
```

Verify:

```bash
curl https://<app>.fermyon.app/health
curl -X POST https://<app>.fermyon.app/echo \
  -H "Content-Type: application/json" -d '{"message":"slice"}'
curl -X POST https://<app>.fermyon.app/echo \
  -H "Content-Type: application/json" -d '{"message":""}'
# → 400 ProblemDetails (DataAnnotations validation)
```

Fermyon Cloud free tier: 5 apps, 100K requests/month, 100 MB component limit.

## Deploy to Cloudflare Workers (Paid plan)

> **Requires [Cloudflare Workers Paid plan](https://dash.cloudflare.com/sign-up/workers)** ($5/month).
> The component is ~3.8 MiB gzip-compressed — over the free-tier 3 MiB limit but within the 10 MiB paid-tier limit.

```bash
cd samples/Slice.WasiSample/dist

# 1. Install JS dependencies (once)
npm install

# 2. Transpile (jco → component/) — assumes wasm already built
npm run transpile

# 3. Login and deploy
npx wrangler login
npm run deploy
```

`npm run build` combines `dotnet publish` + `npm run transpile` in one step.

Verify against the live URL:

```bash
curl https://<worker-name>.<account>.workers.dev/health
curl -X POST https://<worker-name>.<account>.workers.dev/echo \
  -H "Content-Type: application/json" -d '{"message":"slice"}'
```

## Known limitations

- Features returning `IResult` / `Task<IResult>` are excluded from WASI routes (SLICE008 diagnostic).
- Body-binding routes must have `WasiJsonContext` source-generated JSON metadata; routes without it are excluded from WASI routes (SLICE009 diagnostic).
- `[Filter<T>]` filters (other than `SliceValidatorFilter<T>`) do not run in the WASI path — they require ASP.NET's `IEndpointFilter` pipeline.
- WASI DataAnnotations validation is source-generated for `RequiredAttribute`, `StringLengthAttribute`, `MinLengthAttribute` on strings, arrays, and types with a public `Count` property. Other validation rules cause the route to be excluded (SLICE011 diagnostic).
- .NET NativeAOT imports `wasi:sockets/tcp` and `wasi:sockets/udp` at the WASM ABI level even when unused. The Cloudflare transpile pipeline stubs these with `stubs/tcp.js` and `stubs/udp.js`; Spin ignores unused socket imports natively.
- All `Slice.Wasi` dependencies are pre-release. The NuGet experimental feed is configured via `RestoreAdditionalProjectSources` inside the csproj.

## Source map

These files intentionally live in the sample. `Slice.Wasi` owns the reusable runtime pieces (`WasiRequest`, `WasiResponse`, routing, binding, validation, and `WasiApp.DispatchAsync`); the files below are application-specific or deployment-target glue that may later move into CLI scaffolding/templates if multiple apps need the same shape.

| File | Purpose |
| --- | --- |
| [`IncomingHandlerImpl.cs`](IncomingHandlerImpl.cs) | `wasi:http/incoming-handler` → `WasiApp.DispatchAsync` bridge (compiled only under `-r wasi-wasm`; depends on WIT-generated `ProxyWorld` types from the app's componentize-dotnet build) |
| [`WasiJsonContext.cs`](WasiJsonContext.cs) | App-specific `JsonSerializerContext` for AOT/trim-safe serialization |
| [`Features/`](Features/) | `[Feature]` classes — identical shape to ASP.NET features |
| [`spin.toml`](spin.toml) | Fermyon Cloud / Spin deployment manifest |
| [`dist/shim.mjs`](dist/shim.mjs) | Cloudflare Workers fetch handler; bridges `fetch(Request)` → wasi:http |
| [`dist/stubs/tcp.js`](dist/stubs/tcp.js) | Stub for `wasi:sockets/tcp` (ABI import, never called) |
| [`dist/stubs/udp.js`](dist/stubs/udp.js) | Stub for `wasi:sockets/udp` (ABI import, never called) |
| [`dist/wrangler.toml`](dist/wrangler.toml) | Cloudflare deployment config (includes `[build]` trigger) |
| [`dist/wrangler.deploy.toml`](dist/wrangler.deploy.toml) | Deploy-only config (no `[build]`; use after manual build) |
