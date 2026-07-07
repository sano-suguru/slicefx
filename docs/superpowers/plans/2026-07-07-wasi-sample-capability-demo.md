# WasiSample KV + HttpClient Capability Demo — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add legible WASI sample features that exercise `IKeyValueStore` and `IWasiHttpClient` through their in-memory doubles, with in-process `DispatchAsync` tests, closing the dogfooding gap.

**Architecture:** Extract app-building into a `SampleWasiApp.Create()` factory compiled in all builds (the wasm entry point and a new test project both call it, so tested and deployed wiring cannot drift). Add a `Notes` KV feature group (`PUT`/`GET`/`GET list`/`DELETE /notes/{id}`) and a `Fetch` HttpClient feature (`POST /fetch`), returning `SliceResult<T>` / non-generic `SliceResult`.

**Tech Stack:** .NET 10, `SliceFx.Wasi` + `SliceFx.Wasi.KeyValue` + `SliceFx.Wasi.HttpClient`, source-generated WASI route table, xUnit v3, System.Text.Json source-gen (`WasiJsonContext`).

**Spec:** `docs/superpowers/specs/2026-07-07-wasi-sample-capability-demo-design.md`

## Global Constraints

- Target `net10.0`; `LangVersion=latest`; `TreatWarningsAsErrors`; `EnforceCodeStyleInBuild`. Build must be **0 warnings**.
- Style (CI-enforced via `dotnet format --severity info`): file-scoped namespaces, `var`, 4-space indent, LF line endings, final newline.
- **No `Guid.NewGuid()` / `System.Security.Cryptography`** anywhere reachable under `wasi-wasm` (absent in NativeAOT-LLVM). Note IDs are client-supplied.
- **Register in-memory doubles via the instance overload** (`AddKeyValueStore(store)` / `AddWasiHttpClient(client)`) — the `<T>()` overloads are `[RequiresDynamicCode]`.
- **`SliceFx.Core` stays zero-dependency** — do not add package references to it (not touched here).
- Every body-binding request type and every `SliceResult<T>` payload `T` must be `[JsonSerializable]` in `WasiJsonContext` (for `SliceResult<T>`, register `T`, not the wrapper), else SLICE021 excludes the route.
- WASI validation attributes limited to `Required`, `StringLength`, `Url` (supported; avoids SLICE022).
- New test project inherits CPM (`Directory.Packages.props`) — **no versions on `PackageReference`**.
- Branch: `feat/wasi-sample-kv-httpclient-demo` (already created).

---

## Task 1: Scaffolding — satellite refs, shared factory, test project (smoke test)

Prove the whole chain the rubber-duck verified manually: a plain test project references the sample, calls `SampleWasiApp.Create()`, and dispatches an existing route (`GET /health`) in-process.

**Files:**
- Modify: `samples/SliceFx.WasiSample/SliceFx.WasiSample.csproj` (add 2 ProjectReferences)
- Create: `samples/SliceFx.WasiSample/SampleWasiApp.cs`
- Modify: `samples/SliceFx.WasiSample/IncomingHandlerExportsImpl.cs:20-26` (`CreateApp` delegates to factory)
- Create: `tests/SliceFx.WasiSample.Tests/SliceFx.WasiSample.Tests.csproj`
- Create: `tests/SliceFx.WasiSample.Tests/SmokeTests.cs`
- Modify: `SliceFx.slnx` (register the test project)

**Interfaces:**
- Produces: `SliceFx.WasiSample.SampleWasiApp.Create(IKeyValueStore? keyValueStore = null, IWasiHttpClient? httpClient = null) → WasiApp`; `SampleWasiApp.DemoFetchUrl` (const string). `WasiApp.DispatchAsync(WasiRequest, CancellationToken) → Task<WasiResponse>` (existing). `WasiRequest(string Method, string Path, IReadOnlyDictionary<string,string> Headers, string? QueryString, byte[]? Body)` (existing). `WasiResponse(int Status, IReadOnlyDictionary<string,string> Headers, byte[] Body)` (existing).

- [ ] **Step 1: Add satellite ProjectReferences to the sample**

In `samples/SliceFx.WasiSample/SliceFx.WasiSample.csproj`, extend the existing `<ItemGroup>` that lists `ProjectReference`s (currently `SliceFx.Core`, `SliceFx.Wasi`, `SliceFx.SourceGenerator`) — add these two lines alongside the `SliceFx.Wasi` reference:

```xml
    <ProjectReference Include="..\..\src\SliceFx.Wasi.KeyValue\SliceFx.Wasi.KeyValue.csproj" />
    <ProjectReference Include="..\..\src\SliceFx.Wasi.HttpClient\SliceFx.Wasi.HttpClient.csproj" />
```

- [ ] **Step 2: Create the shared factory**

Create `samples/SliceFx.WasiSample/SampleWasiApp.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;

namespace SliceFx.WasiSample;

/// <summary>
/// Builds the WASI application for this sample. Extracted here (compiled in every build) so the
/// wasm entry point and the in-process test project share one wiring definition and cannot drift.
/// </summary>
public static class SampleWasiApp
{
    /// <summary>
    /// Demo URL that the seeded in-memory HTTP client answers with canned HTML. It is NOT a real
    /// outbound request — on Spin / Cloudflare, <see cref="IWasiHttpClient"/> is backed by a
    /// wasi:http/outgoing-handler implementation (see the slicefx-inbox app for the WIT-bound layer).
    /// </summary>
    public const string DemoFetchUrl = "https://slicefx.example/hello";

    /// <summary>
    /// Builds a fresh <see cref="WasiApp"/> with the sample's service wiring. Returns a NEW app on
    /// every call (no cached singleton) so in-process tests get isolated in-memory state; the wasm
    /// entry point caches its own singleton. Pass overrides to inject test doubles.
    /// </summary>
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

    // Demo/test double: canned HTML for DemoFetchUrl only. Real outbound HTTP is host-provided.
    private static InMemoryWasiHttpClient CreateSeededHttpClient() =>
        new InMemoryWasiHttpClient().Respond(
            request => request.Url == DemoFetchUrl,
            new WasiResponse(
                200,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "text/html" },
                "<html><head><title>Hello from SliceFx</title></head><body>hi</body></html>"u8.ToArray()));
}
```

- [ ] **Step 3: Delegate the wasm entry point to the factory**

In `samples/SliceFx.WasiSample/IncomingHandlerExportsImpl.cs`, replace the `CreateApp()` method body (lines 20-26) so it delegates to the shared factory. The `private static readonly WasiApp _app = CreateApp();` field (line 18) stays — the singleton lives here, not in the factory:

```csharp
    private static WasiApp CreateApp() => SliceFx.WasiSample.SampleWasiApp.Create();
```

Remove the now-unused `using Microsoft.Extensions.DependencyInjection;` only if it is no longer referenced elsewhere in the file (it is used for `AddSingleton` — since that moves out, check: the file no longer calls `AddSingleton`, so remove that using to avoid IDE0005). Verify no other `Microsoft.Extensions.DependencyInjection` usage remains in the file before removing.

- [ ] **Step 4: Create the test project**

Create `tests/SliceFx.WasiSample.Tests/SliceFx.WasiSample.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
    <Using Include="SliceFx" />
    <Using Include="SliceFx.Wasi" />
    <Using Include="SliceFx.WasiSample" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\samples\SliceFx.WasiSample\SliceFx.WasiSample.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Register the test project in the solution**

In `SliceFx.slnx`, inside the `<Folder Name="/tests/">` block, add (after the `SliceFx.Wasi.IntegrationTests` line):

```xml
    <Project Path="tests/SliceFx.WasiSample.Tests/SliceFx.WasiSample.Tests.csproj" />
```

- [ ] **Step 6: Write the smoke test**

Create `tests/SliceFx.WasiSample.Tests/SmokeTests.cs`:

```csharp
namespace SliceFx.WasiSample.Tests;

public sealed class SmokeTests
{
    private static WasiRequest Get(string path) =>
        new("GET", path, new Dictionary<string, string>(), QueryString: null, Body: null);

    [Fact]
    public async Task Factory_builds_and_dispatches_health_route()
    {
        await using var app = SampleWasiApp.Create();

        var response = await app.DispatchAsync(Get("/health"), TestContext.Current.CancellationToken);

        Assert.Equal(200, response.Status);
    }
}
```

- [ ] **Step 7: Run the smoke test to verify it passes**

Run: `dotnet test tests/SliceFx.WasiSample.Tests -c Release`
Expected: PASS (1 test). This confirms restore of the sample reference is clean, the factory compiles in a non-wasi build, and the source-generated `WasiRouteTable` dispatches in-process.

- [ ] **Step 8: Verify the sample still builds green**

Run: `dotnet build samples/SliceFx.WasiSample -c Release`
Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 9: Commit**

```bash
git add samples/SliceFx.WasiSample/SliceFx.WasiSample.csproj \
        samples/SliceFx.WasiSample/SampleWasiApp.cs \
        samples/SliceFx.WasiSample/IncomingHandlerExportsImpl.cs \
        tests/SliceFx.WasiSample.Tests/ SliceFx.slnx
git commit -m "feat(wasi): extract SampleWasiApp factory + WasiSample test project"
```

---

## Task 2: Notes — create & read (`PutNote`, `GetNote`)

**Files:**
- Create: `samples/SliceFx.WasiSample/Features/Notes/NoteView.cs`
- Create: `samples/SliceFx.WasiSample/Features/Notes/PutNote.cs`
- Create: `samples/SliceFx.WasiSample/Features/Notes/GetNote.cs`
- Modify: `samples/SliceFx.WasiSample/WasiJsonContext.cs` (register `NoteView`, `PutNote.Request`)
- Test: `tests/SliceFx.WasiSample.Tests/NotesFeatureTests.cs`

**Interfaces:**
- Consumes: `SampleWasiApp.Create()`; `IKeyValueStore.ExistsAsync/SetJsonAsync/GetJsonAsync`; `SliceResult<T>.Ok/Created/NotFound`; `WasiJsonContext.Default.NoteView`.
- Produces: `SliceFx.WasiSample.Features.Notes.NoteView(string Id, string Title, string? Body, DateTimeOffset UpdatedAt)`; `PutNote.Request(string Title, string? Body)`. Key scheme `note:{id}`.

- [ ] **Step 1: Create the shared note record + register it**

Create `samples/SliceFx.WasiSample/Features/Notes/NoteView.cs`:

```csharp
namespace SliceFx.WasiSample.Features.Notes;

/// <summary>
/// A stored note. Serialized into the key-value store and returned by the Notes features,
/// so the same shape round-trips through <c>IKeyValueStore</c> and the HTTP response.
/// </summary>
/// <param name="Id">Client-supplied note identifier (the <c>{id}</c> route segment).</param>
/// <param name="Title">Note title.</param>
/// <param name="Body">Optional note body.</param>
/// <param name="UpdatedAt">UTC timestamp of the last write, from the injected clock.</param>
public sealed record NoteView(string Id, string Title, string? Body, DateTimeOffset UpdatedAt);
```

In `samples/SliceFx.WasiSample/WasiJsonContext.cs`, add these attribute lines above `[SliceJsonContext(SliceJsonTarget.Wasi)]`:

```csharp
[JsonSerializable(typeof(Features.Notes.NoteView), TypeInfoPropertyName = "NoteView")]
[JsonSerializable(typeof(Features.Notes.PutNote.Request), TypeInfoPropertyName = "PutNoteRequest")]
```

(The `PutNote.Request` reference will not compile until Step 3 creates `PutNote`; that is fine — Steps 1+3 land together before the Step 5 build. If working strictly incrementally, create the `PutNote` file skeleton in Step 3 before building.)

- [ ] **Step 2: Write the failing tests**

Create `tests/SliceFx.WasiSample.Tests/NotesFeatureTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using SliceFx.WasiSample.Features.Notes;

namespace SliceFx.WasiSample.Tests;

public sealed class NotesFeatureTests
{
    private static readonly IReadOnlyDictionary<string, string> JsonHeaders =
        new Dictionary<string, string> { ["Content-Type"] = "application/json" };

    private static WasiRequest Put(string id, string json) =>
        new("PUT", $"/notes/{id}", JsonHeaders, QueryString: null, Body: Encoding.UTF8.GetBytes(json));

    private static WasiRequest Get(string path) =>
        new("GET", path, new Dictionary<string, string>(), QueryString: null, Body: null);

    private static NoteView ReadNote(WasiResponse response) =>
        JsonSerializer.Deserialize(response.Body, WasiJsonContext.Default.NoteView)!;

    [Fact]
    public async Task Put_new_note_returns_201()
    {
        await using var app = SampleWasiApp.Create();
        var ct = TestContext.Current.CancellationToken;

        var response = await app.DispatchAsync(Put("a1", """{"title":"First","body":"hello"}"""), ct);

        Assert.Equal(201, response.Status);
        Assert.Contains(response.Headers, h => string.Equals(h.Key, "Location", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Put_existing_note_returns_200()
    {
        await using var app = SampleWasiApp.Create();
        var ct = TestContext.Current.CancellationToken;
        await app.DispatchAsync(Put("a1", """{"title":"First"}"""), ct);

        var response = await app.DispatchAsync(Put("a1", """{"title":"Updated"}"""), ct);

        Assert.Equal(200, response.Status);
        Assert.Equal("Updated", ReadNote(response).Title);
    }

    [Fact]
    public async Task Get_after_put_round_trips()
    {
        await using var app = SampleWasiApp.Create();
        var ct = TestContext.Current.CancellationToken;
        await app.DispatchAsync(Put("a1", """{"title":"First","body":"hello"}"""), ct);

        var response = await app.DispatchAsync(Get("/notes/a1"), ct);

        Assert.Equal(200, response.Status);
        var note = ReadNote(response);
        Assert.Equal("a1", note.Id);
        Assert.Equal("First", note.Title);
        Assert.Equal("hello", note.Body);
    }

    [Fact]
    public async Task Get_missing_note_returns_404()
    {
        await using var app = SampleWasiApp.Create();

        var response = await app.DispatchAsync(Get("/notes/nope"), TestContext.Current.CancellationToken);

        Assert.Equal(404, response.Status);
    }
}
```

- [ ] **Step 3: Implement `PutNote` and `GetNote`**

Create `samples/SliceFx.WasiSample/Features/Notes/PutNote.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using SliceFx.Wasi.KeyValue;

namespace SliceFx.WasiSample.Features.Notes;

/// <summary>Creates or updates a note at the client-supplied id (idempotent upsert).</summary>
[Feature("PUT /notes/{id}", Summary = "Create or update a note")]
public static class PutNote
{
    /// <summary>Request body for creating or updating a note.</summary>
    /// <param name="Title">Note title (required, 1-200 chars).</param>
    /// <param name="Body">Optional note body.</param>
    public record Request([Required, StringLength(200, MinimumLength = 1)] string Title, string? Body);

    /// <summary>Upserts the note into the key-value store under <c>note:{id}</c>.</summary>
    public static async Task<SliceResult<NoteView>> Handle(
        string id, Request req, IKeyValueStore kv, TimeProvider clock, CancellationToken ct)
    {
        var key = $"note:{id}";
        var existed = await kv.ExistsAsync(key, ct);
        var view = new NoteView(id, req.Title, req.Body, clock.GetUtcNow());
        await kv.SetJsonAsync(key, view, WasiJsonContext.Default.NoteView, ct);
        return existed
            ? SliceResult<NoteView>.Ok(view)
            : SliceResult<NoteView>.Created(view, $"/notes/{id}");
    }
}
```

Create `samples/SliceFx.WasiSample/Features/Notes/GetNote.cs`:

```csharp
using SliceFx.Wasi.KeyValue;

namespace SliceFx.WasiSample.Features.Notes;

/// <summary>Reads a single note by id from the key-value store.</summary>
[Feature("GET /notes/{id}", Summary = "Get a note by id")]
public static class GetNote
{
    /// <summary>Returns the stored note, or 404 when the key is absent.</summary>
    public static async Task<SliceResult<NoteView>> Handle(string id, IKeyValueStore kv, CancellationToken ct)
    {
        var view = await kv.GetJsonAsync($"note:{id}", WasiJsonContext.Default.NoteView, ct);
        return view is null
            ? SliceResult<NoteView>.NotFound($"Note '{id}' not found.")
            : SliceResult<NoteView>.Ok(view);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/SliceFx.WasiSample.Tests -c Release --filter "FullyQualifiedName~NotesFeatureTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Verify sample builds green**

Run: `dotnet build samples/SliceFx.WasiSample -c Release`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 6: Commit**

```bash
git add samples/SliceFx.WasiSample/Features/Notes/NoteView.cs \
        samples/SliceFx.WasiSample/Features/Notes/PutNote.cs \
        samples/SliceFx.WasiSample/Features/Notes/GetNote.cs \
        samples/SliceFx.WasiSample/WasiJsonContext.cs \
        tests/SliceFx.WasiSample.Tests/NotesFeatureTests.cs
git commit -m "feat(wasi): Notes KV demo — PutNote + GetNote (create/read)"
```

---

## Task 3: Notes — list & delete (`ListNotes`, `DeleteNote`)

**Files:**
- Create: `samples/SliceFx.WasiSample/Features/Notes/ListNotes.cs`
- Create: `samples/SliceFx.WasiSample/Features/Notes/DeleteNote.cs`
- Modify: `samples/SliceFx.WasiSample/WasiJsonContext.cs` (register `ListNotes.Response`)
- Test: `tests/SliceFx.WasiSample.Tests/NotesFeatureTests.cs` (add list/delete tests)

**Interfaces:**
- Consumes: `IKeyValueStore.ListKeysAsync/GetJsonAsync/ExistsAsync/DeleteAsync`; `SliceResult<T>.Ok`; non-generic `SliceResult.NoContent/NotFound`; `NoteView`; `WasiJsonContext.Default.NoteListResponse`.
- Produces: `ListNotes.Response(IReadOnlyList<NoteView> Notes)`.

- [ ] **Step 1: Register the list response type**

In `samples/SliceFx.WasiSample/WasiJsonContext.cs`, add above `[SliceJsonContext(...)]`:

```csharp
[JsonSerializable(typeof(Features.Notes.ListNotes.Response), TypeInfoPropertyName = "NoteListResponse")]
```

- [ ] **Step 2: Write the failing tests**

Append these methods to the `NotesFeatureTests` class in `tests/SliceFx.WasiSample.Tests/NotesFeatureTests.cs`:

```csharp
    private static WasiRequest Delete(string id) =>
        new("DELETE", $"/notes/{id}", new Dictionary<string, string>(), QueryString: null, Body: null);

    [Fact]
    public async Task List_returns_all_notes()
    {
        await using var app = SampleWasiApp.Create();
        var ct = TestContext.Current.CancellationToken;
        await app.DispatchAsync(Put("a1", """{"title":"First"}"""), ct);
        await app.DispatchAsync(Put("a2", """{"title":"Second"}"""), ct);

        var response = await app.DispatchAsync(Get("/notes"), ct);

        Assert.Equal(200, response.Status);
        var list = JsonSerializer.Deserialize(response.Body, WasiJsonContext.Default.NoteListResponse)!;
        Assert.Equal(2, list.Notes.Count);
        Assert.Contains(list.Notes, n => n.Id == "a1");
        Assert.Contains(list.Notes, n => n.Id == "a2");
    }

    [Fact]
    public async Task Delete_existing_note_returns_204_then_get_404()
    {
        await using var app = SampleWasiApp.Create();
        var ct = TestContext.Current.CancellationToken;
        await app.DispatchAsync(Put("a1", """{"title":"First"}"""), ct);

        var deleteResponse = await app.DispatchAsync(Delete("a1"), ct);
        var getResponse = await app.DispatchAsync(Get("/notes/a1"), ct);

        Assert.Equal(204, deleteResponse.Status);
        Assert.Equal(404, getResponse.Status);
    }

    [Fact]
    public async Task Delete_missing_note_returns_404()
    {
        await using var app = SampleWasiApp.Create();

        var response = await app.DispatchAsync(Delete("nope"), TestContext.Current.CancellationToken);

        Assert.Equal(404, response.Status);
    }
```

- [ ] **Step 3: Implement `ListNotes` and `DeleteNote`**

Create `samples/SliceFx.WasiSample/Features/Notes/ListNotes.cs`:

```csharp
using SliceFx.Wasi.KeyValue;

namespace SliceFx.WasiSample.Features.Notes;

/// <summary>Lists all stored notes via a prefix scan of the key-value store.</summary>
[Feature("GET /notes", Summary = "List all notes")]
public static class ListNotes
{
    /// <summary>Wraps the note collection so the WASI JSON root is a named object.</summary>
    /// <param name="Notes">All stored notes.</param>
    public record Response(IReadOnlyList<NoteView> Notes);

    /// <summary>Scans keys under the <c>note:</c> prefix and returns each stored note.</summary>
    public static async Task<SliceResult<Response>> Handle(IKeyValueStore kv, CancellationToken ct)
    {
        var keys = await kv.ListKeysAsync(ct);
        var notes = new List<NoteView>();
        foreach (var key in keys)
        {
            if (!key.StartsWith("note:", StringComparison.Ordinal))
            {
                continue;
            }

            var view = await kv.GetJsonAsync(key, WasiJsonContext.Default.NoteView, ct);
            if (view is not null)
            {
                notes.Add(view);
            }
        }

        return SliceResult<Response>.Ok(new Response(notes));
    }
}
```

Create `samples/SliceFx.WasiSample/Features/Notes/DeleteNote.cs`:

```csharp
using SliceFx.Wasi.KeyValue;

namespace SliceFx.WasiSample.Features.Notes;

/// <summary>Deletes a note by id. Returns 204 on success, 404 when absent.</summary>
[Feature("DELETE /notes/{id}", Summary = "Delete a note by id")]
public static class DeleteNote
{
    /// <summary>Removes <c>note:{id}</c> from the store; non-generic result (no body on success).</summary>
    public static async Task<SliceResult> Handle(string id, IKeyValueStore kv, CancellationToken ct)
    {
        var key = $"note:{id}";
        if (!await kv.ExistsAsync(key, ct))
        {
            return SliceResult.NotFound($"Note '{id}' not found.");
        }

        await kv.DeleteAsync(key, ct);
        return SliceResult.NoContent();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/SliceFx.WasiSample.Tests -c Release --filter "FullyQualifiedName~NotesFeatureTests"`
Expected: PASS (7 tests total).

- [ ] **Step 5: Verify sample builds green**

Run: `dotnet build samples/SliceFx.WasiSample -c Release`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 6: Commit**

```bash
git add samples/SliceFx.WasiSample/Features/Notes/ListNotes.cs \
        samples/SliceFx.WasiSample/Features/Notes/DeleteNote.cs \
        samples/SliceFx.WasiSample/WasiJsonContext.cs \
        tests/SliceFx.WasiSample.Tests/NotesFeatureTests.cs
git commit -m "feat(wasi): Notes KV demo — ListNotes + DeleteNote (list/delete)"
```

---

## Task 4: Fetch — outbound HTTP (`PostFetch`)

**Files:**
- Create: `samples/SliceFx.WasiSample/Features/Fetch/PostFetch.cs`
- Modify: `samples/SliceFx.WasiSample/WasiJsonContext.cs` (register `PostFetch.Request`, `PostFetch.Response`)
- Test: `tests/SliceFx.WasiSample.Tests/FetchFeatureTests.cs`

**Interfaces:**
- Consumes: `SampleWasiApp.Create(httpClient: ...)`; `IWasiHttpClient.SendAsync(WasiHttpRequest, ct) → WasiResponse`; `WasiHttpRequest(string Method, string Url, IReadOnlyDictionary<string,string>? Headers, byte[]? Body)`; `WasiHttpException`; `InMemoryWasiHttpClient.Respond(...)`; `SliceResult<T>.Ok/Problem`; `SampleWasiApp.DemoFetchUrl`.
- Produces: `PostFetch.Request(string Url)`; `PostFetch.Response(string Url, int Status, string? Title)`.

- [ ] **Step 1: Register the fetch request/response types**

In `samples/SliceFx.WasiSample/WasiJsonContext.cs`, add above `[SliceJsonContext(...)]`:

```csharp
[JsonSerializable(typeof(Features.Fetch.PostFetch.Request), TypeInfoPropertyName = "PostFetchRequest")]
[JsonSerializable(typeof(Features.Fetch.PostFetch.Response), TypeInfoPropertyName = "PostFetchResponse")]
```

- [ ] **Step 2: Write the failing tests**

Create `tests/SliceFx.WasiSample.Tests/FetchFeatureTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using SliceFx.Wasi.HttpClient;

namespace SliceFx.WasiSample.Tests;

public sealed class FetchFeatureTests
{
    private static readonly IReadOnlyDictionary<string, string> JsonHeaders =
        new Dictionary<string, string> { ["Content-Type"] = "application/json" };

    private static WasiRequest PostFetch(string json) =>
        new("POST", "/fetch", JsonHeaders, QueryString: null, Body: Encoding.UTF8.GetBytes(json));

    [Fact]
    public async Task Fetch_demo_url_extracts_title()
    {
        await using var app = SampleWasiApp.Create();

        var response = await app.DispatchAsync(
            PostFetch($$"""{"url":"{{SampleWasiApp.DemoFetchUrl}}"}"""),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.Status);
        var result = JsonSerializer.Deserialize(response.Body, WasiJsonContext.Default.PostFetchResponse)!;
        Assert.Equal("Hello from SliceFx", result.Title);
    }

    [Fact]
    public async Task Fetch_invalid_url_returns_400()
    {
        await using var app = SampleWasiApp.Create();

        var response = await app.DispatchAsync(
            PostFetch("""{"url":"not-a-url"}"""),
            TestContext.Current.CancellationToken);

        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task Fetch_upstream_error_returns_502()
    {
        // Inject a client that returns a non-2xx for any URL to exercise the error path.
        var failing = new InMemoryWasiHttpClient().Respond(
            _ => true,
            new WasiResponse(500, new Dictionary<string, string>(), []));
        await using var app = SampleWasiApp.Create(httpClient: failing);

        var response = await app.DispatchAsync(
            PostFetch("""{"url":"https://example.com/"}"""),
            TestContext.Current.CancellationToken);

        Assert.Equal(502, response.Status);
    }
}
```

- [ ] **Step 3: Implement `PostFetch`**

Create `samples/SliceFx.WasiSample/Features/Fetch/PostFetch.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.Text;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;

namespace SliceFx.WasiSample.Features.Fetch;

/// <summary>
/// Fetches a URL through <see cref="IWasiHttpClient"/> and extracts its HTML title — a miniature
/// of the slicefx-inbox og:title use case. In this sample the client is an in-memory double with
/// a canned response; on Spin / Cloudflare it is backed by wasi:http/outgoing-handler.
/// </summary>
[Feature("POST /fetch", Summary = "Fetch a URL and extract its title")]
public static class PostFetch
{
    /// <summary>Request body for the fetch endpoint.</summary>
    /// <param name="Url">Absolute URL to fetch.</param>
    public record Request([Required, Url] string Url);

    /// <summary>Result of the fetch.</summary>
    /// <param name="Url">The requested URL.</param>
    /// <param name="Status">Upstream HTTP status code.</param>
    /// <param name="Title">Extracted &lt;title&gt; text, or null when absent.</param>
    public record Response(string Url, int Status, string? Title);

    /// <summary>Performs the outbound GET and maps upstream failures to 502.</summary>
    public static async Task<SliceResult<Response>> Handle(Request req, IWasiHttpClient http, CancellationToken ct)
    {
        WasiResponse upstream;
        try
        {
            upstream = await http.SendAsync(new WasiHttpRequest("GET", req.Url, Headers: null, Body: null), ct);
        }
        catch (WasiHttpException ex)
        {
            return SliceResult<Response>.Problem(502, "Bad Gateway", ex.Message);
        }

        if (upstream.Status is < 200 or > 299)
        {
            return SliceResult<Response>.Problem(502, "Bad Gateway", $"Upstream returned {upstream.Status}.");
        }

        return SliceResult<Response>.Ok(new Response(req.Url, upstream.Status, ExtractTitle(upstream.Body)));
    }

    // Minimal, dependency-free title extraction (no regex, no crypto) — WASI-safe.
    private static string? ExtractTitle(byte[] body)
    {
        var html = Encoding.UTF8.GetString(body);
        var open = html.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
        if (open < 0)
        {
            return null;
        }

        var start = open + "<title>".Length;
        var close = html.IndexOf("</title>", start, StringComparison.OrdinalIgnoreCase);
        return close < 0 ? null : html[start..close].Trim();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/SliceFx.WasiSample.Tests -c Release --filter "FullyQualifiedName~FetchFeatureTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Verify sample builds green**

Run: `dotnet build samples/SliceFx.WasiSample -c Release`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 6: Commit**

```bash
git add samples/SliceFx.WasiSample/Features/Fetch/PostFetch.cs \
        samples/SliceFx.WasiSample/WasiJsonContext.cs \
        tests/SliceFx.WasiSample.Tests/FetchFeatureTests.cs
git commit -m "feat(wasi): Fetch HttpClient demo — PostFetch (outbound GET + title extraction)"
```

---

## Task 5: Documentation

**Files:**
- Modify: `samples/SliceFx.WasiSample/README.md`
- Modify: `docs/patterns/platform-abstraction.md`
- Modify: `CLAUDE.md`

**Interfaces:** none (docs only).

- [ ] **Step 1: Update the sample README — new features section + source map**

In `samples/SliceFx.WasiSample/README.md`, after the "Deploy to Fermyon Cloud" verify block (the `curl .../echo` examples), add a new subsection:

````markdown
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
````

Then in the **Source map** table, add three rows and **fix the stale `IncomingHandlerImpl.cs` name**:

- Change the existing first row's `` [`IncomingHandlerImpl.cs`](IncomingHandlerImpl.cs) `` to `` [`IncomingHandlerExportsImpl.cs`](IncomingHandlerExportsImpl.cs) ``.
- Add:

```markdown
| [`SampleWasiApp.cs`](SampleWasiApp.cs) | Shared app factory (`AddSlice` + in-memory KV/HttpClient/TimeProvider); compiled in all builds so the wasm entry point and the test project share one wiring definition |
| [`Features/Notes/`](Features/Notes/) | `IKeyValueStore` demo — PUT/GET/GET-list/DELETE `/notes/{id}` |
| [`Features/Fetch/`](Features/Fetch/) | `IWasiHttpClient` demo — POST `/fetch` (outbound GET + title extraction) |
```

- [ ] **Step 2: Update platform-abstraction.md**

In `docs/patterns/platform-abstraction.md`:

(a) Fix the stale filename in the WASI code example header (~line 57): change `**WASI `IncomingHandlerImpl.cs`:**` to `**WASI `IncomingHandlerExportsImpl.cs`:**`.

(b) In the "Capability implementation cost" paragraph (~line 125), the sentence currently reads (find this exact text):

```
This second layer is documented via XML doc-comments on each interface plus in-process unit tests (`SliceFx.Wasi.KeyValue.Tests`, `SliceFx.Wasi.HttpClient.Tests`, `SliceFx.Wasi.Spin.Tests`), and `SliceFx.Wasi.Spin` additionally carries `README.md` sample code.
```

Replace it with:

```
The first layer is demonstrated end-to-end in `samples/SliceFx.WasiSample` (the `Notes` features use `IKeyValueStore`, `POST /fetch` uses `IWasiHttpClient`, both wired to the in-memory doubles) and covered by in-process unit tests (`SliceFx.Wasi.KeyValue.Tests`, `SliceFx.Wasi.HttpClient.Tests`, `SliceFx.Wasi.Spin.Tests`); `SliceFx.Wasi.Spin` additionally carries `README.md` sample code. The second, WIT-bound layer is documented via XML doc-comments on each interface.
```

(The following sentence about `slicefx-inbox` being the reference for the WIT-bound layer stays unchanged.)

- [ ] **Step 3: Update CLAUDE.md satellite sections**

In `CLAUDE.md`, in the `### SliceFx.Wasi.KeyValue` section, append to the sentence ending `use `InMemoryKeyValueStore` in tests.`:

```
 `samples/SliceFx.WasiSample` (the `Notes` feature group) demonstrates in-process usage against the in-memory double.
```

In the `### SliceFx.Wasi.HttpClient` section, append to the sentence ending `Use `InMemoryWasiHttpClient` in tests.`:

```
 `samples/SliceFx.WasiSample` (`POST /fetch`) demonstrates in-process usage against the in-memory double.
```

- [ ] **Step 4: Commit**

```bash
git add samples/SliceFx.WasiSample/README.md docs/patterns/platform-abstraction.md CLAUDE.md
git commit -m "docs(wasi): document WasiSample KV + HttpClient capability demos"
```

---

## Task 6: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Full solution build (0 warnings)**

Run: `dotnet build SliceFx.slnx -c Release`
Expected: Build succeeded, 0 Warning(s), 0 Error(s). (Warnings are errors under `TreatWarningsAsErrors`, so a warning fails the build.)

- [ ] **Step 2: Full test suite**

Run: `dotnet test SliceFx.slnx -c Release`
Expected: all test projects pass, including the new `SliceFx.WasiSample.Tests` (11 tests: 1 smoke + 7 notes + 3 fetch).

- [ ] **Step 3: Format gate (matches CI)**

Run: `dotnet format SliceFx.slnx --verify-no-changes --severity info --exclude-diagnostics CS1591 xUnit1004`
Expected: no output, exit code 0. If it reports changes, run without `--verify-no-changes` to apply, review, and amend the relevant commit.

- [ ] **Step 4 (optional, macOS): WASI publish sanity via Docker**

Confirms the two new satellite references + features compile and trim under `wasi-wasm`. Skip if Docker is unavailable — the `wasi-wasm-publish.yml` CI gate covers it.

Run:
```bash
docker run --rm --platform linux/amd64 -v "$PWD":/work -w /work \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet publish samples/SliceFx.WasiSample -r wasi-wasm -c Release
```
Expected: publish succeeds and `samples/SliceFx.WasiSample/dist/slice-wasi-sample.wasm` is produced/updated.

- [ ] **Step 5: Finish the branch**

Use the `superpowers:finishing-a-development-branch` skill to decide merge vs PR. Suggested PR title: `feat(wasi): KV + HttpClient satellite demos in WasiSample (B4)`.

---

## Self-Review

**Spec coverage:**
- Shared factory (compiled all builds, fresh app per call, optional overrides) → Task 1 Step 2. ✓
- wasm entry point delegates to factory, singleton stays there → Task 1 Step 3. ✓
- Satellite ProjectReferences → Task 1 Step 1. ✓
- New test project + slnx + first sample-referencing precedent → Task 1 Steps 4-5. ✓
- Notes PUT/GET/LIST/DELETE covering all 5 `IKeyValueStore` methods (Exists, SetJson→SetBytes, GetJson→GetBytes, ListKeys, Delete) → Tasks 2-3. ✓
- `SliceResult<T>` (Notes) + non-generic `SliceResult` (Delete) → Tasks 2-3. ✓
- Fetch outbound + title extraction + 502 error path + seeded canned client → Task 4. ✓
- Client-supplied id / no Guid / no crypto → PutNote uses route id; title extraction uses IndexOf. ✓
- Instance-overload registration → factory uses `AddKeyValueStore(store)` / `AddWasiHttpClient(client)`. ✓
- WasiJsonContext registrations (NoteView, PutNote.Request, ListNotes.Response, PostFetch.Request/Response; `SliceResult<T>` registers T) → Tasks 2-4 Step 1. ✓
- Docs: README (features + source map + stale-filename fix), platform-abstraction.md (paragraph + stale-filename fix), CLAUDE.md → Task 5. Top README untouched. ✓
- Verification: build/test/format/optional-wasm → Task 6. ✓

**Placeholder scan:** No TBD/TODO; every code step has complete code; every command has expected output. ✓

**Type consistency:** `SampleWasiApp.Create(IKeyValueStore?, IWasiHttpClient?)` and `DemoFetchUrl` used identically across Tasks 1/4. `NoteView(Id,Title,Body,UpdatedAt)` consistent across Tasks 2/3 and README example. `ListNotes.Response(Notes)` matches `WasiJsonContext.Default.NoteListResponse` accessor. `PostFetch.Request(Url)` / `PostFetch.Response(Url,Status,Title)` match `PostFetchRequest`/`PostFetchResponse` accessors. `WasiRequest`/`WasiResponse`/`WasiHttpRequest` constructor argument orders match the source. ✓
