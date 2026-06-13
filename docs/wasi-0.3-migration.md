# WASI 0.3 Migration Tracking

> **Status: upstream-blocked — do not start** (verified 2026-06-13)
>
> Migration is gated entirely on the C# guest toolchain shipping WASI 0.3 support.
> No code changes should be made until the unblock conditions below are met.

## Why this note exists

The Bytecode Alliance ratified [WASI 0.3](https://bytecodealliance.org/articles/WASI-0.3) with
Component Model native async: `wasi:io` pollable/stream absorbed into the canonical ABI,
`wasi:http` reorganised from the `proxy` world into `service` / `middleware` worlds, and the
handler signature changed to `handle: async func(request) -> result<response, error-code>`.

SliceFx's entire WASI stack targets **0.2.0** (proxy world, synchronous handler, poll-based I/O).
A migration is needed in the future, but as of the verification date above:

- **componentize-dotnet `v0.8.0-preview00011`** (released 2026-06-12) is the latest version —
  the same version currently pinned in `SliceFx.WasiSample.csproj`.
  It bundles wit-bindgen **0.58.0** / wasm-tools 1.251.0 / wac v0.10.0 / wasmtime 45.
- This release has **no WASI 0.3 support**: no `service`/`middleware` world bindings, no C#
  `async func` export codegen. Zero open PR/Issue tagged "0.3" in the repo.
- The announcement itself says "C# async guest binding support is in-progress."
  Wasmtime 45/46 running 0.3 RC on the *host* side is unrelated to *guest* codegen.

## What changes in WASI 0.3

| WASI 0.2 | WASI 0.3 |
|---|---|
| `world proxy` | `world service` (HTTP server) / `world middleware` (can forward to another handler) |
| `resource pollable` | `future<T>` |
| `resource input-stream` | `stream<u8>` |
| `resource output-stream` | `stream<u8>` (written-to direction) |
| `poll(list<pollable>)` | `await` on a future (runtime-managed) |
| `start-foo / finish-foo` dance | `foo: async func(...)` |
| `handle: func(request, response-out)` (sync void) | `handle: async func(request) -> result<response, error-code>` |
| `read-via-stream: func() -> result<input-stream, error-code>` | `read-via-stream: func() -> tuple<stream<u8>, future<result<_, error-code>>>` |

Once C# bindings emit idiomatic `async` code, the current
`.GetAwaiter().GetResult()` blocking dispatch in `IncomingHandlerImpl.cs` can be replaced
with a genuine async path.

The `middleware` world supersedes the 0.2 `proxy` world and enables **service chaining** —
direct in-process component composition without network hops.

> **Fermyon/Spin interfaces are independent of WASI.**
> `fermyon:spin/*@2.0.0` and `spin:cron@3.0.0` are Spin's own versioned interfaces and will
> not change as part of a WASI 0.3 migration. Follow them separately when Spin updates.

## Unblock conditions (착수 트리거)

All three must be true before starting:

1. **componentize-dotnet ships WASI 0.3 service/middleware world bindings** — a new release
   that emits 0.3 WIT namespaces for `wasi:http/handler` and generates C# `async func` exports
   without `// TODO` stubs or unimplemented `future`/`stream` paths. This is the hard gate.
2. **jco ships 0.3 as default** — required for the Cloudflare Workers deployment path
   (`samples/SliceFx.WasiSample/dist/`).
3. **Target runtimes accept 0.3 components** — Fermyon Spin and/or Cloudflare Workers.
   General: wasmtime 46+ (currently on wasmtime 45).

## Monitoring

- [componentize-dotnet releases](https://github.com/bytecodealliance/componentize-dotnet/releases) — watch for a release whose notes mention WASI 0.3 or `service`/`middleware` worlds
- wit-bindgen C# async-export codegen quality — check generated `.cs` files for `// TODO` and incomplete `future`/`stream` branch arms
- [jco releases](https://github.com/bytecodealliance/jco/releases) — watch for 0.3 default-enabled
- [Fermyon Spin changelog](https://github.com/fermyon/spin/blob/main/CHANGELOG.md) — WASI 0.3 component support
- [Cloudflare Workers WASI changelog](https://developers.cloudflare.com/workers/platform/changelog/) — 0.3 runtime support

## Files to change when unblocked

### `samples/SliceFx.WasiSample/`

| File | Change |
|---|---|
| `SliceFx.WasiSample.csproj` | `<Wit ... World="service" Registry="ghcr.io/webassembly/wasi/http:0.3.0">` (proxy → service or middleware) |
| `IncomingHandlerImpl.cs` | `v0_2_0` namespace → new 0.3 namespace; sync `void Handle(IncomingRequest, ResponseOutparam)` → async `handler` implementation; body I/O via `stream<u8>` + `future` instead of `input-stream` / `BlockingRead` |

### `tools/SliceFx.Cli/Templates/WasiCloudflareTemplate.cs`

- `jco transpile` flags and `wasi:sockets/*@0.2.0` stub references

### Package `<Description>` strings

Four csproj files advertising `"wasi:http/incoming-handler@0.2.0"`:
`src/SliceFx.Wasi/`, `SliceFx.Wasi.HttpClient/`, `SliceFx.Wasi.KeyValue/`, `SliceFx.Wasi.Spin/`

### `src/SliceFx.SourceGenerator/Emit/WasiRegistrationEmitter.cs`

Probably **no change needed** — it emits pure C# registration/dispatch code with no WIT
dependency. Revisit only if `WasiApp.DispatchAsync` gains a true async implementation.

### `src/SliceFx.Wasi.Spin/`

Cron trigger stays sync (`spin:cron@3.0.0` is unchanged). Re-evaluate the sync-vs-async export
choice after C# async-export codegen matures.
