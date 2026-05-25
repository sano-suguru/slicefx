# Platform abstraction with Slice

The `builder.Services` DI container is the consistent seam across every Slice host. The same service interface — and the same feature file — can be registered with different implementations per deployment target.

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
using Slice.Lambda;
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
using Slice.Wasi;
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

`Slice.TestHost` exposes the same `builder.Services` seam for test substitution. The optional configure callback runs after the app's own service registrations, so only the listed services are replaced:

```csharp
await using var host = SliceTestHost.Create<global::Program>(svc =>
    svc.Replace<IProductStore>(new FakeProductStore()));

var resp = await host.Client.GetAsync($"/products/{id}");
```

See `samples/Slice.TestHostSample/` for a runnable example of this pattern.

## Portability and storage

Portability classification is determined by the handler return type and filter types, not by which DI services are injected. A feature that receives a DynamoDB-backed store is still `portable` if it returns a plain record. Running `slice routes` will reflect this:

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

This feature is excluded from WASI route tables (`SLICE008`) and function-per-feature Lambda, but it is a full Minimal API endpoint on the ASP.NET and hosted Lambda path. Mixing `portable` and `aspnet-only` features in the same project is the expected pattern, not an error condition.
