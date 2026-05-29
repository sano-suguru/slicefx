# Platform abstraction with SliceFx

The `builder.Services` DI container is the consistent seam across every SliceFx host. The same service interface — and the same feature file — can be registered with different implementations per deployment target.

## The pattern

Define a service interface in your project:

```csharp
public interface IProductStore
{
    Task<Product?> GetAsync(Guid id, CancellationToken ct);
    Task<Product> AddAsync(string name, CancellationToken ct);
}
```

Write the feature once against that interface:

```csharp
[Feature("GET /products/{id:guid}")]
public static class GetProduct
{
    public record Response(Guid Id, string Name);

    public static async Task<Response?> Handle(Guid id, IProductStore store, CancellationToken ct)
    {
        var product = await store.GetAsync(id, ct);
        return product is null ? null : new Response(product.Id, product.Name);
    }
}
```

Register a different implementation depending on the host:

**ASP.NET-hosted `Program.cs`:**
```csharp
var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddSlice();
builder.Services.AddSingleton<IProductStore, InMemoryProductStore>();
var app = builder.Build();
app.MapSlices();
app.Run();
```

**Lambda `Program.cs`:**
```csharp
using SliceFx.Lambda;
var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddSlice();
builder.Services.AddSingleton<IProductStore, DynamoDbProductStore>();
builder.UseSliceLambda();
var app = builder.Build();
app.MapSlices();
await app.RunOnLambdaAsync();
```

**WASI `IncomingHandlerImpl.cs`:**
```csharp
using SliceFx.Wasi;
private static WasiApp CreateApp()
{
    var builder = WasiHost.CreateBuilder();
    builder.AddSlice();
    builder.Services.AddSingleton<IProductStore, KvProductStore>();
    return builder.Build();
}
```

The feature file `GetProduct.cs` is unchanged across all three hosts.

## Testing with a swap

`SliceFx.TestHost` exposes the same `builder.Services` seam for test substitution. The optional configure callback runs after the app's own service registrations, so only the listed services are replaced:

```csharp
await using var host = SliceTestHost.Create<global::Program>(svc =>
    svc.Replace<IProductStore>(new FakeProductStore()));

var resp = await host.Client.GetAsync($"/products/{id}");
```

See `samples/SliceFx.TestHostSample/` for a runnable example of this pattern.

## Portability and storage

Portability classification is determined by the handler return type and filter types, not by which DI services are injected. A feature that receives a DynamoDB-backed store is still `portable` if it returns a plain record. Running `slicefx routes` will reflect this:

```
GET /products/{id:guid}   portable   GetProduct
```

This means the same feature can run on ASP.NET with in-memory storage during development, Lambda with DynamoDB in staging, and WASI with a stub in tests — without any code changes to the feature file itself.

## Platform-specific features

When a feature needs a platform-specific capability with no portable equivalent (for example, Workers Durable Objects or Lambda-specific invocation context), model it as an `aspnet-only` or WASI-only feature by using the appropriate return type. Use DI to keep the platform-specific logic out of the feature file itself:

```csharp
// aspnet-only feature: returns IResult to access NotFound()
[Feature("DELETE /products/{id:guid}")]
public static class DeleteProduct
{
    public static async Task<IResult> Handle(Guid id, IProductStore store, CancellationToken ct)
    {
        if (!await store.RemoveAsync(id, ct)) return Results.NotFound();
        return Results.NoContent();
    }
}
```

This feature is excluded from WASI route tables (`SLICE020`) and function-per-feature Lambda, but it is a full Minimal API endpoint on the ASP.NET and hosted Lambda path. Mixing `portable` and `aspnet-only` features in the same project is the expected pattern, not an error condition.

## WASI implementation notes

These constraints affect all implementations that target NativeAOT-LLVM WASI (via componentize-dotnet):

**WIT-generated bindings:** componentize-dotnet emits WIT-bound free functions on `*Interop` static classes. For example, `fermyon:spin/variables@2.0.0` generates `VariablesInterop.Get(string name)` — not an instance method. The generated `IVariables` type carries only the `Error` shape and is not the call entry point. Satellite libraries (`SliceFx.Wasi.Spin`, `SliceFx.Wasi.KeyValue`) expose pure-interface abstractions over these; the concrete WIT-bound implementation lives in the application and is wired via DI.

**Async surface over sync WIT:** WIT host calls are synchronous, but satellite interfaces use `ValueTask`-returning methods to match the repo's async convention and remain compatible with future async providers. Wrap synchronous WIT calls with `ValueTask.FromResult(...)` in application implementations.

**`System.Security.Cryptography` is not available** in NativeAOT-LLVM WASI builds. This includes `CryptographicOperations.FixedTimeEquals`, `HMACSHA256`, and all classes in the `System.Security.Cryptography` namespace. For constant-time comparisons (e.g. token authentication), use a manual XOR-accumulation loop:</p>

```csharp
// Constant-time string comparison without System.Security.Cryptography
static bool SafeEquals(string? a, string? b)
{
    if (a is null || b is null) return false;
    var ab = System.Text.Encoding.UTF8.GetBytes(a);
    var bb = System.Text.Encoding.UTF8.GetBytes(b);
    if (ab.Length != bb.Length) return false;
    var diff = 0;
    for (var i = 0; i < ab.Length; i++) diff |= ab[i] ^ bb[i];
    return diff == 0;
}
```
