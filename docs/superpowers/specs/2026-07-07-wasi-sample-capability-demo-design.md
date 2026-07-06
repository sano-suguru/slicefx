# B4: KV + HttpClient satellite demo in WasiSample — Design

**Status:** approved (brainstorming) — 2026-07-07
**Task:** B4

## Problem

`SliceFx.Wasi.KeyValue` (`IKeyValueStore`) and `SliceFx.Wasi.HttpClient` (`IWasiHttpClient`)
ship in `src/`, have unit tests, and are used in production by the external `slicefx-inbox`
app. But **no in-repo sample demonstrates using either satellite from a WASI feature**.
`samples/SliceFx.WasiSample` currently registers only `TimeProvider`. A new user has nowhere
in the framework to see "how do I read/write KV or make an outbound HTTP call from a WASI
feature." B4 closes that dogfooding gap.

## Goal & non-goals

**Goal:** Add a small, legible set of WASI features to `SliceFx.WasiSample` that exercise both
capability satellites through their in-memory doubles, plus in-process `DispatchAsync` tests,
so the wiring is both demonstrated and regression-covered. The `wasi-wasm` publish gate stays
green (the doubles are WIT-free) and now additionally compiles/trims the two satellites.

**Design philosophy (agreed):** The sample demonstrates the **first layer** — the thin interface,
DI wiring, and in-memory double. It does **not** ship the **second layer** — the real WIT-bound
implementation (`wasi:keyvalue/store@0.2.0-draft`, `wasi:http/outgoing-handler@0.2.0`). The sample
points to `slicefx-inbox` as the worked example of the second layer.

**Non-goals (out of scope, unchanged from brief):**
- PromoteUser XML doc drift fix (`samples/SliceFx.Sample/Features/Users/PromoteUser.cs`).
- WasiSample `<DebugType>none` wasm size optimization.
- Per-feature WASM packaging.

## Key constraints (verified against the codebase)

- **App-building is wasm-only today.** `IncomingHandlerExportsImpl.cs` holds `CreateApp()`
  (`WasiHost.CreateBuilder()` → `AddSlice()` → service registration), and the whole file is
  `<Compile Remove>`'d unless `RuntimeIdentifier == wasi-wasm`. In-process tests cannot build
  a wasm target, so the wiring must be extracted into a file compiled in **all** builds.
- **Reference safety (empirically verified via a throwaway project):** a plain `net10.0` test
  project can `ProjectReference` `SliceFx.WasiSample` and restore/build/dispatch. All wasm-only
  items (`BytecodeAlliance.Componentize.DotNet.Wasm.SDK`, `ilcompiler.llvm`,
  `RestoreAdditionalProjectSources`, `Wit`) are gated on `RuntimeIdentifier == wasi-wasm`, so
  they do not leak into a non-wasi consumer. The source generator is referenced
  `ReferenceOutputAssembly="false"`, so it runs only inside WasiSample; the consumer uses the
  already-compiled `WasiRouteTable`.
- **No `Guid.NewGuid()` / crypto in WASI.** `System.Security.Cryptography` is absent under
  NativeAOT-LLVM WASI. Note IDs are client-supplied (`PUT /notes/{id}`), avoiding server-side RNG.
- **In-memory doubles must be registered via the instance overload.** `AddKeyValueStore<T>()`
  and `AddWasiHttpClient<T>()` reflection overloads carry `[RequiresDynamicCode]` /
  `[RequiresUnreferencedCode]`; the factory `new`s the concrete doubles and uses the instance
  overloads (`AddKeyValueStore(store)`, `AddWasiHttpClient(client)`).
- **WASI JSON:** every body-binding request type and every `SliceResult<T>` payload `T` must be
  registered in `WasiJsonContext` (`[SliceJsonContext(SliceJsonTarget.Wasi)]`), or the route is
  excluded (SLICE021). For `SliceResult<T>`, register `T`, not the wrapper.
- **WASI validation:** stay within source-gen-supported attributes (`Required`, `StringLength`,
  `Url`) so no route is excluded via SLICE022.
- Style/CI: file-scoped namespaces, `var`, 4-space indent, LF, final newline,
  `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, 0-warning build, `dotnet format` gate.
  New test project inherits CPM (`Directory.Packages.props`) — no versions on `PackageReference`.

## Architecture

### Shared app factory (the structural crux)

New `samples/SliceFx.WasiSample/SampleWasiApp.cs`, compiled in **all** builds:

```csharp
public static class SampleWasiApp
{
    // Demo URL the seeded HTTP client answers with canned HTML (see README).
    public const string DemoFetchUrl = "https://slicefx.example/hello";

    // Returns a FRESH WasiApp each call (no cached singleton) so in-process tests get
    // isolated in-memory state. The wasm entry point caches its own singleton.
    public static WasiApp Create(
        IKeyValueStore? keyValueStore = null,
        IWasiHttpClient? httpClient = null)
    {
        var builder = WasiHost.CreateBuilder();
        builder.AddSlice();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.AddKeyValueStore(keyValueStore ?? new InMemoryKeyValueStore());
        builder.AddWasiHttpClient(httpClient ?? CreateSeededHttpClient());
        return builder.Build();
    }

    // Demo/test double: canned HTML for DemoFetchUrl. NOT a real outbound call —
    // on Spin/Cloudflare this is replaced by a wasi:http/outgoing-handler impl (see slicefx-inbox).
    private static InMemoryWasiHttpClient CreateSeededHttpClient() =>
        new InMemoryWasiHttpClient().Respond(
            r => r.Url == DemoFetchUrl,
            new WasiResponse(200,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "text/html" },
                "<html><head><title>Hello from SliceFx</title></head><body>hi</body></html>"u8.ToArray()));
}
```

`IncomingHandlerExportsImpl.CreateApp()` becomes:

```csharp
private static WasiApp CreateApp() => SampleWasiApp.Create();
```

(the `private static readonly WasiApp _app = CreateApp();` static-field singleton stays in
`IncomingHandlerExportsImpl` — the factory itself never caches).

Rationale: the factory is the single source of truth for wiring, so the tested path and the
deployed path cannot drift (a missing KV registration fails a test, not just 500s in production).
The optional parameters let a test inject a client that returns a non-2xx / throws, to cover the
`/fetch` error path — the wasm path never passes them.

### Features (5 new `[Feature]` classes)

Shared note view (stored in KV **and** returned), `Features/Notes/NoteView.cs`:

```csharp
public sealed record NoteView(string Id, string Title, string? Body, DateTimeOffset UpdatedAt);
```

KV group — `Features/Notes/` (key scheme `note:{id}`):

| Feature (route) | Satellite methods | Return |
|---|---|---|
| `PutNote` — `PUT /notes/{id}` | `ExistsAsync` (→ 201 vs 200) + `SetJsonAsync` | `SliceResult<NoteView>` `.Created`/`.Ok` |
| `GetNote` — `GET /notes/{id}` | `GetJsonAsync` | `SliceResult<NoteView>` `.Ok`/`.NotFound` |
| `ListNotes` — `GET /notes` | `ListKeysAsync` + prefix-scan + `GetJsonAsync` | `SliceResult<NoteListResponse>` `.Ok` |
| `DeleteNote` — `DELETE /notes/{id}` | `ExistsAsync` + `DeleteAsync` | `SliceResult` (non-generic) `.NoContent`/`.NotFound` |

- `PutNote.Request(`[Required, StringLength(200, MinimumLength = 1)]` string Title, string? Body)`;
  handler receives `string id, Request req, IKeyValueStore kv, TimeProvider clock, CancellationToken ct`.
- `ListNotes` returns `NoteListResponse(IReadOnlyList<NoteView> Notes)` (own file/nested); it lists
  keys, keeps those starting with `note:`, strips the prefix, and `GetJsonAsync`'s each.
- KV JSON uses `WasiJsonContext.Default.NoteView` for get/set (reflection-free).

HttpClient group — `Features/Fetch/`:

| Feature (route) | Behavior | Return |
|---|---|---|
| `PostFetch` — `POST /fetch` | `SendAsync` GET the url → extract `<title>` (`IndexOf`, no regex) | `SliceResult<FetchResult>` `.Ok`; on `WasiHttpException` or non-2xx → `.Problem(502, "Bad Gateway", …)` |

- `PostFetch.Request(`[Required, Url]` string Url)`; `FetchResult(string Url, int Status, string? Title)`.
- Title extraction: decode body UTF-8, find `<title>` / `</title>` case-insensitively; `null` if absent.

### WasiJsonContext additions

Register (with `TypeInfoPropertyName`): `NoteView`, `NoteListResponse`, `PutNote.Request`,
`PostFetch.Request`, `FetchResult`. (`DeleteNote` returns non-generic `SliceResult` — no `T`.)

### Tests — new `tests/SliceFx.WasiSample.Tests/`

Plain `net10.0` xUnit v3 (`OutputType=Exe`), CPM-inherited, `ProjectReference` to
`SliceFx.WasiSample`, `<Using Include="SliceFx" />` + `<Using Include="SliceFx.Wasi" />` +
`<Using Include="SliceFx.WasiSample" />`. Added to `SliceFx.slnx` `/tests/` folder so
`dotnet test SliceFx.slnx` covers it. This is the **first** test project that references a sample
(a deliberate new precedent; verified restore-clean).

Pattern: `var app = SampleWasiApp.Create(...); var resp = await app.DispatchAsync(new WasiRequest(...));`
then assert on `resp.Status` and (deserializing with `WasiJsonContext.Default.<T>`) `resp.Body`.

- `NotesFeatureTests`: PUT-new → 201 + Location; PUT-existing → 200; GET → 200 round-trip;
  GET missing → 404; LIST after several PUTs → all present; DELETE → 204 then GET → 404;
  DELETE missing → 404.
- `FetchFeatureTests`: `/fetch` `DemoFetchUrl` → 200 + `Title == "Hello from SliceFx"`;
  invalid/missing url → 400 ProblemDetails; error path via injected client returning 500 → 502.

### Documentation ("most correct" footprint)

- `samples/SliceFx.WasiSample/README.md`: document Notes + Fetch (curl + expected responses);
  state plainly that the in-memory HTTP client returns **canned** data and real outbound HTTP
  needs the WIT-bound impl (→ `slicefx-inbox`); add source-map rows for `SampleWasiApp.cs` +
  `Features/Notes/` + `Features/Fetch/`; **fix the stale `IncomingHandlerImpl.cs` →
  `IncomingHandlerExportsImpl.cs`** source-map/link.
- `docs/patterns/platform-abstraction.md`: update the "Capability implementation cost" paragraph
  (~line 125) — it currently claims first-layer usage is shown only via unit tests + Spin's README
  and that "the satellite interfaces themselves ship no host-specific implementation in this
  repository"; add `WasiSample` as the in-repo first-layer worked example for KV + HttpClient.
  Fix the stale `IncomingHandlerImpl.cs` reference (~line 57).
- `CLAUDE.md`: one clause each in the KV / HttpClient satellite sections noting
  `samples/SliceFx.WasiSample` demonstrates in-process usage with the in-memory doubles.
- Top-level `README.md`: **no change** — its package/sample tables stay accurate.

## Verification

- `dotnet build SliceFx.slnx -c Release` → 0 warnings.
- `dotnet test SliceFx.slnx -c Release`.
- `dotnet format SliceFx.slnx --verify-no-changes --severity info --exclude-diagnostics CS1591 xUnit1004`.
- WASI publish sanity (confirms the two new satellite refs + features compile/trim under wasm):
  `docker run --rm --platform linux/amd64 -v "$PWD":/work -w /work mcr.microsoft.com/dotnet/sdk:10.0 dotnet publish samples/SliceFx.WasiSample -r wasi-wasm -c Release`
  — run locally if Docker is available; otherwise the `wasi-wasm-publish.yml` CI gate covers it.

## Risks

- **Trim under wasm.** Both satellites are pure-managed / WIT-free (verified); instance-overload
  registration keeps them trim-safe. Low risk; the wasm publish sanity step confirms.
- **New precedent (test → sample reference).** Technically verified clean; documented here so
  reviewers expect it.
