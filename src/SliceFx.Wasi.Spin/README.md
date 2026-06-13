# SliceFx.Wasi.Spin

Spin-specific WASI satellite for [SliceFx](https://github.com/sanosuguru/slicefx). Provides abstractions for two Fermyon Spin host capabilities:

- **`ISpinCronHandler`** — handle periodic cron triggers (`fermyon:spin/trigger.cron`)
- **`ISpinVariables`** — read Spin application variables (`fermyon:spin/variables@2.0.0`)

Both are registered via `WasiHostBuilderSpinExtensions` — pure manual DI, not source-generator-driven.

> **Experimental.** APIs may change between preview releases.

## Cron triggers

### 1. Implement `ISpinCronHandler`

```csharp
using SliceFx.Wasi.Spin;

public sealed class FeedRefreshCronHandler : ISpinCronHandler
{
    private readonly IFeedRefresher _refresher;

    public FeedRefreshCronHandler(IFeedRefresher refresher) => _refresher = refresher;

    public async ValueTask OnTickAsync(SpinCronContext context, CancellationToken ct = default)
    {
        Console.Error.WriteLine($"[CronTick] FireTime={context.FireTime:u}");
        await _refresher.RefreshAllAsync(ct);
    }
}
```

`SpinCronContext` carries only `FireTime` — the `spin:cron@3.0.0` WIT provides `{ timestamp: u64 }` with no metadata field.

### 2. Register in `WasiHost`

```csharp
var builder = WasiHost.CreateBuilder();
builder.AddSlice();
builder.AddSpinCronHandler<FeedRefreshCronHandler>();
builder.Services.AddSingleton<IFeedRefresher, SpinFeedRefresher>();
var app = builder.Build();
```

### 3. Wire the WIT cron export entry point

componentize-dotnet generates a world-level `handle-cron-event` export wired via the `IProxyWorld` pattern — distinct from the `wasi:http/incoming-handler` export that `IIncomingHandlerExports` implements. Add a static entry point in your WIT-only compilation unit:

```csharp
// CronHandlerBridge.cs — compiled only under -r wasi-wasm
using ProxyWorld.wit.Imports.fermyon.spin.v3_0_0;
using SliceFx.Wasi.Spin;

namespace MyApp;

// The WIT runtime calls this static method for each cron tick.
public static class CronHandlerBridge
{
    private static WasiApp? _app;

    public static void HandleCronEvent(ICronTrigger.CronEvent cronEvent)
    {
        _app ??= WasiAppFactory.Create();
        var fireTime = DateTimeOffset.FromUnixTimeSeconds((long)cronEvent.Timestamp);
        var context = new SpinCronContext(fireTime);
        // async func export encoding was fixed in componentize-dotnet 0.8.0 / wit-bindgen 0.58,
        // but C# async-export codegen is still preview quality; keep sync func + GetAwaiter().GetResult().
        SpinCronDispatcher.DispatchAsync(_app, context, CancellationToken.None)
            .GetAwaiter().GetResult();
    }
}
```

> **`async func` export encoding was fixed in componentize-dotnet 0.8.0 / wit-bindgen 0.58**, but C# async-export codegen is still preview quality — generated code contains `// TODO` and unimplemented `future`/`stream` paths. SliceFx recommends keeping the WIT cron export as a plain `func` (sync entry point) and calling `.GetAwaiter().GetResult()` to drive the async handler synchronously until the upstream codegen stabilises.

### Cron expression format

`spin:cron` uses **6-field** expressions: `{sec} {min} {hour} {dom} {month} {dow}`.  
A 7-field expression causes a `ParseSchedule` error at Spin startup:

```toml
[[trigger.cron]]
component = "my-app"
cron_expression = "0 */30 * * * *"   # every 30 minutes (6 fields)
# cron_expression = "0 */30 * * * * *"  # WRONG — 7 fields → ParseSchedule error
```

### Testing cron handlers

Use `RecordingSpinCronHandler` to capture invocations without a running Spin runtime:

```csharp
[Fact]
public async Task CronTick_RefreshesFeeds()
{
    var handler = new RecordingSpinCronHandler();
    var app = WasiHost.CreateBuilder()
        .AddSpinCronHandler(handler)
        .Build();
    var context = new SpinCronContext(DateTimeOffset.UtcNow);

    await SpinCronDispatcher.DispatchAsync(app, context, TestContext.Current.CancellationToken);

    Assert.Single(handler.Invocations);
}
```

---

## Spin variables

### 1. Implement `ISpinVariables`

On Fermyon Cloud / Spin, implement using the WIT-generated binding. componentize-dotnet generates free functions on a `*Interop` static class (the `IVariables` type carries only the `Error` shape):

```csharp
// SpinVariables.cs — compiled only under -r wasi-wasm
using SliceFx.Wasi.Spin;
using VariablesImportsInterop = ProxyWorld.wit.Imports.fermyon.spin.v2_0_0.VariablesImportsInterop;
using IVariablesImports = ProxyWorld.wit.Imports.fermyon.spin.v2_0_0.IVariablesImports;

public sealed class SpinVariablesImpl : ISpinVariables
{
    public ValueTask<string?> GetAsync(string name, CancellationToken ct = default)
    {
        try
        {
            // VariablesImportsInterop.Get is the WIT-generated free function;
            // IVariablesImports.Error holds the error type, not the call entry point.
            return ValueTask.FromResult<string?>(VariablesImportsInterop.Get(name));
        }
        catch (Exception ex) when (ex is WitException or ProxyWorld.WitException)
        {
            Console.Error.WriteLine($"[Variables] Could not read '{name}': {ex.Message}");
            return ValueTask.FromResult<string?>(null);  // fail-closed
        }
    }
}
```

### 2. Register in `WasiHost`

```csharp
var builder = WasiHost.CreateBuilder();
builder.AddSlice();
builder.AddSpinVariables<SpinVariablesImpl>();  // WASI build
// or builder.AddSpinVariables(new InMemorySpinVariables(...));  // tests / non-WASI
var app = builder.Build();
```

### 3. Declare the variable in `spin.toml`

```toml
[variables]
refresh_token = { secret = true }

[component.my-app.variables]
refresh_token = "{{ refresh_token }}"
```

### Testing with `InMemorySpinVariables`

```csharp
var vars = new InMemorySpinVariables(
    new Dictionary<string, string> { ["refresh_token"] = "test-secret" });

var app = WasiHost.CreateBuilder()
    .AddSpinVariables(vars)
    .Build();
```

---

## Source map

| File | Purpose |
| --- | --- |
| [`ISpinCronHandler.cs`](ISpinCronHandler.cs) | Interface: handle a single cron tick |
| [`SpinCronContext.cs`](SpinCronContext.cs) | Record: `FireTime` (timestamp from `spin:cron@3.0.0`) |
| [`SpinCronDispatcher.cs`](SpinCronDispatcher.cs) | Static helper: resolve `ISpinCronHandler` from DI and invoke |
| [`RecordingSpinCronHandler.cs`](RecordingSpinCronHandler.cs) | Test double: records invocations, exposes `Invocations` + `Clear()` |
| [`ISpinVariables.cs`](ISpinVariables.cs) | Interface: read Spin application variables (fail-closed, async surface) |
| [`InMemorySpinVariables.cs`](InMemorySpinVariables.cs) | Test double: in-memory dictionary, `Set` / `Clear` helpers |
| [`WasiHostBuilderSpinExtensions.cs`](WasiHostBuilderSpinExtensions.cs) | `AddSpinCronHandler` + `AddSpinVariables` DI registration |
