# Slice.WorkersSample

This sample shows Slice `[Feature]` classes — unmodified from their ASP.NET counterparts — running as a Cloudflare Workers deployment via a WASI 0.2 component. The same feature handles requests whether the host is ASP.NET Core, AWS Lambda, or Cloudflare Workers.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (preview/RC accepted; SDK 10.0.300 pinned in `global.json`)
- [Node.js](https://nodejs.org/) 18+ (for `jco` transpilation and the local dev shim)
- Linux x64 or Windows x64 for `wasi-wasm` publish with the current NativeAOT-LLVM preview packages
- A [Cloudflare account](https://dash.cloudflare.com/sign-up) and `wrangler` (deploy steps only)

**Preview dependencies** (these are experimental; build may break when upstream releases change):

| Dependency | Version |
| --- | --- |
| `BytecodeAlliance.Componentize.DotNet.Wasm.SDK` | `0.7.0-preview00010` |
| `runtime.<host>.microsoft.dotnet.ilcompiler.llvm` | `10.0.0-alpha.1.25162.1` (Linux x64 / Windows x64 host package) |
| `@bytecodealliance/jco` | `1.19.0` |
| `@bytecodealliance/preview2-shim` | `0.17.9` |

## In-process probe

Verify routing and validation without building a WASM component:

```bash
dotnet run --project samples/Slice.WorkersSample -- --probe /health
# [probe] dispatching GET /health  →  status=200

dotnet run --project samples/Slice.WorkersSample -- --probe /echo
# [probe] dispatching POST /echo {"message":"hello"}  →  status=200
# [probe] dispatching POST /echo {"message":""}       →  status=400  (DataAnnotations validation)
```

## Local dev (Workers shim)

Build a WASI 0.2 component and run it through the Cloudflare Workers JavaScript shim on Node.js:

```bash
# 1. Install JS dependencies (once)
cd samples/Slice.WorkersSample/worker
npm install

# 2. Build, transpile, and start the dev server
npm run build   # dotnet publish → jco transpile → component/
npm run dev     # node dev-server.mjs, listens on http://localhost:8787
```

Verify:

```bash
curl http://localhost:8787/health
curl -X POST http://localhost:8787/echo \
  -H "Content-Type: application/json" -d '{"message":"slice"}'
curl -X POST http://localhost:8787/echo \
  -H "Content-Type: application/json" -d '{"message":""}'
# → 400 ProblemDetails (DataAnnotations validation runs on Workers too)
```

## Deploy to Cloudflare

> **Requires [Cloudflare Workers Paid plan](https://dash.cloudflare.com/sign-up/workers).**
> The compiled worker (WASM + JS shim) is ~3.8 MiB gzip-compressed — over the free-tier 3 MiB limit but well within the paid-tier 10 MiB limit. The paid plan is $5/month.

```bash
cd samples/Slice.WorkersSample/worker

# 1. Build the WASM component (if not already done from Local dev above)
dotnet publish .. -r wasi-wasm -c Release
npm install
npm run transpile   # jco transpile → component/  (skips dotnet publish)

# 2. Login once
npx wrangler login

# 3. Deploy (skips the build step — WASM already built above)
npm run deploy
```

`npm run deploy` uses `wrangler.deploy.toml` which omits the `[build]` trigger (so it won't re-run `dotnet publish`). On success, Wrangler prints the deployed URL.

Verify against the live URL:

```bash
curl https://<worker-name>.<account>.workers.dev/health
curl -X POST https://<worker-name>.<account>.workers.dev/echo \
  -H "Content-Type: application/json" -d '{"message":"slice"}'
curl -X POST https://<worker-name>.<account>.workers.dev/echo \
  -H "Content-Type: application/json" -d '{"message":""}'
# → 400 ProblemDetails  (DataAnnotations runs on Workers, same as ASP.NET)
```

**`account_id`**: The sample does not commit an account ID. Wrangler will use your logged-in account, or you can export `CLOUDFLARE_ACCOUNT_ID` when deploying.

## Known limitations

- Features returning `IResult` / `Task<IResult>` are excluded from Workers routes. The source generator emits a `SLICE008` info diagnostic for each excluded feature. See [`Features/`](Features/) — only features with POCO or `SliceResult` returns appear in the Workers route table.
- `[Filter<T>]` filters other than `SliceValidatorFilter<T>` are **not** executed in the Workers path — they require ASP.NET's `IEndpointFilter` pipeline. DataAnnotations validation runs in both paths.
- **Workers Paid plan required for `wrangler deploy`**. A .NET NativeAOT WASM binary is inherently large (~12-15 MB before compression); after `wasm-opt -Oz` and gzip the bundle is ~3.8 MiB, which exceeds the 3 MiB free-tier limit but fits within the 10 MiB paid-tier limit.
- Workers DataAnnotations support is source-generated for `RequiredAttribute`, `StringLengthAttribute`, and `MinLengthAttribute` on strings, arrays, and types with a public `Count` property. Routes that require reflection-based validation are excluded from the Workers route table with a generator warning.
- `.NET` NativeAOT imports `wasi:sockets/tcp` at the WASM ABI level even when the app never calls TCP. The transpile pipeline stubs this with `stubs/tcp.js`; the real `preview2-shim/sockets` depends on Node.js worker-thread APIs not available in Cloudflare Workers.
- All `Slice.Workers` dependencies are pre-release (`0.x` / preview). The NuGet experimental feed is configured via `RestoreAdditionalProjectSources` inside the csproj (no separate `NuGet.Config` needed).
- The current NativeAOT-LLVM preview publishes from Linux x64 or Windows x64. macOS can run the in-process probe, but `dotnet publish -r wasi-wasm` should be run from one of the supported publish hosts.

## Source map

| File | Purpose |
| --- | --- |
| [`Program.cs`](Program.cs) | `WorkerHost.CreateBuilder()` bootstrap; `--probe` mode for in-process testing |
| [`WorkerJsonContext.cs`](WorkerJsonContext.cs) | `JsonSerializerContext` for AOT/trim-safe serialization |
| [`Features/`](Features/) | `[Feature]` classes — identical shape to ASP.NET features |
| [`worker/shim.mjs`](worker/shim.mjs) | Cloudflare Workers fetch handler; bridges HTTP → WASI JSON IPC |
| [`worker/component-runner.mjs`](worker/component-runner.mjs) | `preview2-shim` wiring + WASM instantiation for Cloudflare Workers |
| [`worker/stubs/tcp.js`](worker/stubs/tcp.js) | Stub for `wasi:sockets/tcp` (imported by .NET ABI but never called) |
| [`worker/dev-server.mjs`](worker/dev-server.mjs) | Node.js dev server; spawns `jco run` for local testing |
| [`worker/wrangler.toml`](worker/wrangler.toml) | Cloudflare deployment config (includes `[build]` trigger) |
| [`worker/wrangler.deploy.toml`](worker/wrangler.deploy.toml) | Deploy-only config (no `[build]`; use after manual build) |
