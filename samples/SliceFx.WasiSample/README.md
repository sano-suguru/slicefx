# SliceFx.WasiSample

This sample shows Slice `[Feature]` classes — unmodified from their ASP.NET counterparts — compiled to a `wasi:http/incoming-handler` WASM component and deployed to **Cloudflare Workers** (via jco) and **Fermyon Cloud** (via Spin, natively). The same feature handles requests whether the host is ASP.NET Core, AWS Lambda, Cloudflare Workers, or Fermyon Cloud.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (SDK 10.0.300 pinned in `global.json`)
- Linux x64 or Windows x64 for native `wasi-wasm` publish with the current NativeAOT-LLVM preview packages, or Docker with `--platform linux/amd64` on macOS
- [Node.js](https://nodejs.org/) 22+ and `npm` — only for the Cloudflare WASI deployment path
- [Spin CLI](https://developer.fermyon.com/spin/v3/install) — only for the Fermyon Cloud deployment path

## Support status

`SliceFx.Wasi` is experimental: its own 0.x APIs may change before a stable release. The upstream WASI build and deployment toolchain is also unstable: `componentize-dotnet`, NativeAOT-LLVM preview packages, WASI Preview 2, `jco`, `preview2-shim`, Wrangler, and host platform behavior can break independently of SliceFx runtime code. Spin deployment works from the standard `wasi:http/incoming-handler` component produced by this sample; Cloudflare Workers adds a JS transpilation and shim layer, so it has more moving parts.

This sample publishes one WASM component containing the generated route table for all eligible WASI routes. WASI per-feature packaging, such as one component per feature, is not implemented.

**Preview dependencies:**

| Dependency | Version |
| --- | --- |
| `BytecodeAlliance.Componentize.DotNet.Wasm.SDK` | `0.8.0-preview00011` |
| `runtime.<host>.microsoft.dotnet.ilcompiler.llvm` | `10.0.0-rc.1.26306.1` |
| `@bytecodealliance/jco` | `1.22.0` |
| `@bytecodealliance/preview2-shim` | `0.18.1` |
| `binaryen` | `130.0.0` |
| `wrangler` | `4.100.0` |

## Build the WASM component

This publish produces one `wasi:http` component containing all generated WASI routes. It does not split routes into per-feature WASM components.

On Linux x64 or Windows x64:

```bash
dotnet publish samples/SliceFx.WasiSample -r wasi-wasm -c Release
# → copies component to samples/SliceFx.WasiSample/dist/slice-wasi-sample.wasm
```

On macOS, publish through a Linux x64 Docker container:

```bash
docker run --rm --platform linux/amd64 -v "$PWD":/work -w /work \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet publish samples/SliceFx.WasiSample -r wasi-wasm -c Release
```

## Deploy to Fermyon Cloud (free tier)

Fermyon Cloud natively understands `wasi:http/incoming-handler`, so no JS transpilation is needed.

```bash
# 1. Install Spin CLI and log in (once)
spin plugins install cloud --yes
spin cloud login

# 2. Build the component (see above), then deploy
cd samples/SliceFx.WasiSample
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

### Capability demos: KV store and outbound HTTP

Two feature groups show how a WASI feature uses the capability satellites. This sample registers
the **in-memory doubles** (`InMemoryKeyValueStore`, `InMemoryWasiHttpClient`) — they are WIT-free,
so they compile and run in the wasm component. On Fermyon Cloud / Spin / Cloudflare you replace
these with WIT-bound implementations (`wasi:keyvalue/store@0.2.0-draft`,
`wasi:http/outgoing-handler@0.2.0`); the [`slicefx-inbox`](https://github.com/sano-suguru/slicefx-inbox)
app is the reference for that second layer.

**Notes (`SliceFx.Wasi.KeyValue` / `IKeyValueStore`):**

```bash
curl -X PUT https://<app>/notes/hello \
  -H "Content-Type: application/json" -d '{"title":"Hello","body":"world"}'    # 201 Created
curl https://<app>/notes/hello        # 200 {"Id":"hello","Title":"Hello","Body":"world","UpdatedAt":"..."}
curl https://<app>/notes              # 200 {"Notes":[ ... ]}
curl -X DELETE https://<app>/notes/hello   # 204 No Content
```

**Fetch (`SliceFx.Wasi.HttpClient` / `IWasiHttpClient`):**

```bash
curl -X POST https://<app>/fetch \
  -H "Content-Type: application/json" -d '{"url":"https://slicefx.example/hello"}'
# 200 {"Url":"https://slicefx.example/hello","Status":200,"Title":"Hello from SliceFx"}
```

> The in-memory HTTP client only answers the demo URL `https://slicefx.example/hello` with canned
> HTML — it never makes a real network call. It demonstrates the `IWasiHttpClient` feature shape
> (inject the client, extract the `<title>`); real outbound HTTP is provided by the host's WIT
> implementation. Any other URL returns an empty 200 (no title); an upstream non-2xx maps to 502.
>
> **Security note for the WIT-bound layer:** `POST /fetch` forwards a client-supplied URL to
> `IWasiHttpClient`. With the in-memory double this is inert, but a real
> `wasi:http/outgoing-handler` implementation makes it a Server-Side Request Forgery (SSRF)
> surface. If you copy this shape into production, validate the URL against an allowlist, block
> internal/link-local addresses, and cap the response body size before reading it — none of which
> this demo does.

## Deploy to Cloudflare Workers (Paid plan)

> **Requires [Cloudflare Workers Paid plan](https://dash.cloudflare.com/sign-up/workers)** ($5/month).
> The component is ~3.8 MiB gzip-compressed — over the free-tier 3 MiB limit but within the 10 MiB paid-tier limit.

```bash
cd samples/SliceFx.WasiSample/dist

# 1. Install locked JS dependencies (once)
npm ci

# 2. Transpile (jco → component/) — assumes wasm already built
npm run transpile

# 3. Login and deploy
npx wrangler login
npm run deploy
```

`npm run build` combines `dotnet publish` + `npm run transpile` in one step.

For directories generated by `slicefx new wasi-cloudflare`, use `npm install` on the first run because the scaffold does not include a lockfile. Review and commit the generated `package-lock.json`, then use `npm ci` for subsequent installs. This checked-in sample already includes `package-lock.json`, so `npm ci` is preferred here.

Verify against the live URL:

```bash
curl https://<worker-name>.<account>.workers.dev/health
curl -X POST https://<worker-name>.<account>.workers.dev/echo \
  -H "Content-Type: application/json" -d '{"message":"slice"}'
```

## Troubleshooting the toolchain

Use the failing command and log prefix to decide where to investigate:

- `dotnet publish` restore/build failures mentioning `BytecodeAlliance.Componentize.DotNet.Wasm.SDK`, NativeAOT, ILC, `runtime.<host>.microsoft.dotnet.ilcompiler.llvm`, WIT generation, or the unsupported-host MSBuild error are usually in the componentize-dotnet / NativeAOT-LLVM layer or host support matrix.
- `npm run transpile` failures mentioning `jco transpile`, `preview2-shim`, `wasm-opt`, Binaryen, or socket stub imports are in the Cloudflare JS transpilation/shim layer.
- `npm run deploy` / `wrangler deploy` failures, account-plan limits, and behavior tied to `compatibility_date = "2024-09-23"` are Cloudflare deployment/runtime concerns.
- `SLICE020`, `SLICE021`, `SLICE022`, route binding behavior, and WASI validation results are SliceFx source-generator or `SliceFx.Wasi` concerns.

When reporting issues, include the command that failed, the pinned dependency versions above, the host OS/architecture, and whether the failure happens before or after `dist/slice-wasi-sample.wasm` is produced.

## Known limitations

- Features returning `IResult` / `Task<IResult>` are excluded from WASI routes (SLICE020 diagnostic).
- JSON body/response routes must have source-generated JSON metadata from a `JsonSerializerContext` marked with `[SliceJsonContext(SliceJsonTarget.Wasi)]`; routes without it are excluded from WASI routes (SLICE021 diagnostic). When the context exists but a type is missing, SLICE021 names the specific type(s). Run `slicefx json-context --check --target wasi` to audit and `--fix` to repair automatically.
- `[Filter<T>]` endpoint filters do not run in the WASI path — they require ASP.NET's `IEndpointFilter` pipeline. Matching `ISliceValidator<T>` implementations are discovered and run by the generated WASI route table.
- WASI DataAnnotations validation is source-generated for `RequiredAttribute`, `StringLengthAttribute`, `MinLengthAttribute`, `MaxLengthAttribute`, numeric `RangeAttribute`, `EmailAddressAttribute`, `UrlAttribute`, and `RegularExpressionAttribute`. Support is shape-conditional: `StringLengthAttribute` applies only to `string` properties, `RangeAttribute` only to numeric types (`int`, `long`, `double`, `float`, `decimal`), and any attribute with a resource or localized error message counts as unsupported. Other validation rules — including `IValidatableObject`, type-level attributes, and supported attribute types in unsupported shapes — cause the route to be excluded (SLICE022 diagnostic). Excluded routes are absent from the WASI route table; there is no per-request reflection fallback.
- .NET NativeAOT imports `wasi:sockets/tcp` and `wasi:sockets/udp` at the WASM ABI level even when unused. The Cloudflare transpile pipeline stubs these with `stubs/tcp.js` and `stubs/udp.js`; Spin ignores unused socket imports natively.
- All upstream WASI build/transpile dependencies are pre-release or tightly version-pinned for reproducibility. The NuGet experimental feed is configured via `RestoreAdditionalProjectSources` inside the csproj.
- `System.Security.Cryptography` is unavailable in NativeAOT-LLVM WASI builds (the namespace is entirely absent). Use a manual XOR-accumulation loop for constant-time comparisons (see `docs/patterns/platform-abstraction.md`).
- The wasm entry point builds the app once in a static field initializer (`IncomingHandlerExportsImpl._app = CreateApp()`). A failure while wiring the app therefore surfaces as a `TypeInitializationException` at component startup, not as a per-request 500. The in-process tests call `SampleWasiApp.Create()` fresh per test, so they exercise the wiring but not this static-initialization failure boundary.

## Source map

These files intentionally live in the sample. `SliceFx.Wasi` owns the reusable runtime pieces (`WasiRequest`, `WasiResponse`, routing, binding, validation, and `WasiApp.DispatchAsync`); the files below are application-specific or deployment-target glue that may later move into CLI scaffolding/templates if multiple apps need the same shape.

| File | Purpose |
| --- | --- |
| [`IncomingHandlerExportsImpl.cs`](IncomingHandlerExportsImpl.cs) | `wasi:http/incoming-handler` → `WasiApp.DispatchAsync` bridge (compiled only under `-r wasi-wasm`; depends on WIT-generated `ProxyWorld` types from the app's componentize-dotnet build) |
| [`WasiJsonContext.cs`](WasiJsonContext.cs) | App-specific `[SliceJsonContext(SliceJsonTarget.Wasi)]` context for AOT/trim-safe serialization |
| [`Features/`](Features/) | `[Feature]` classes — identical shape to ASP.NET features |
| [`SampleWasiApp.cs`](SampleWasiApp.cs) | Shared app factory (`AddSlice` + in-memory KV/HttpClient/TimeProvider); compiled in all builds so the wasm entry point and the test project share one wiring definition |
| [`Features/Notes/`](Features/Notes/) | `IKeyValueStore` demo — PUT/GET/GET-list/DELETE `/notes/{id}` |
| [`Features/Fetch/`](Features/Fetch/) | `IWasiHttpClient` demo — POST `/fetch` (outbound GET + title extraction) |
| [`spin.toml`](spin.toml) | Fermyon Cloud / Spin deployment manifest |
| [`dist/shim.mjs`](dist/shim.mjs) | Cloudflare Workers fetch handler; bridges `fetch(Request)` → wasi:http |
| [`dist/stubs/tcp.js`](dist/stubs/tcp.js) | Stub for `wasi:sockets/tcp` (ABI import, never called) |
| [`dist/stubs/udp.js`](dist/stubs/udp.js) | Stub for `wasi:sockets/udp` (ABI import, never called) |
| [`dist/wrangler.toml`](dist/wrangler.toml) | Cloudflare deployment config (includes `[build]` trigger) |
| [`dist/wrangler.deploy.toml`](dist/wrangler.deploy.toml) | Deploy-only config (no `[build]`; use after manual build) |
