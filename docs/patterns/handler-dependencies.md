# Handler dependency and state patterns

A Slice `Handle` method has no constructor injection (it is a static method). Every dependency arrives as a method parameter. That is fine for simple endpoints but starts to feel noisy once a complex business handler needs five or more dependencies.

Keep `Handle` stateless. If a feature needs state, caching, or coordination with an external resource, model that concern as a DI service and pass it to `Handle` like any other dependency. This keeps Slice's generated output as plain Minimal API registration while still letting you keep feature-local infrastructure in the same source file when that is clearer.

This document shows how to aggregate dependencies and where to put state **without changing Slice itself** — using only standard C# and DI features.

## Recommended pattern: a feature-local `Dependencies` record

Group the dependencies into a single record and register that record as scoped.

### Before (8 dependencies on the signature)

```csharp
[Feature("POST /orders", Summary = "Place an order")]
public static class PlaceOrder
{
    public record Request(Guid CustomerId, IReadOnlyList<Guid> ProductIds);
    public record Response(Guid OrderId, decimal Total);

    public static async Task<Response> Handle(
        Request req,
        IOrderRepository orders,
        IProductRepository products,
        ICustomerRepository customers,
        IPricingService pricing,
        IInventoryService inventory,
        IEmailSender mailer,
        IAuditLog audit,
        ILogger<PlaceOrder> logger,
        TimeProvider clock,
        CancellationToken ct)
    {
        // 8-dependency body
    }
}
```

The signature alone is 11 lines and hard to skim.

### After (3 parameters plus an aggregated record)

```csharp
[Feature("POST /orders", Summary = "Place an order")]
public static class PlaceOrder
{
    public record Request(Guid CustomerId, IReadOnlyList<Guid> ProductIds);
    public record Response(Guid OrderId, decimal Total);

    public sealed record Dependencies(
        IOrderRepository Orders,
        IProductRepository Products,
        ICustomerRepository Customers,
        IPricingService Pricing,
        IInventoryService Inventory,
        IEmailSender Mailer,
        IAuditLog Audit,
        ILogger<PlaceOrder> Logger,
        TimeProvider Clock);

    public static async Task<Response> Handle(Request req, Dependencies deps, CancellationToken ct)
    {
        var (orders, products, customers, pricing, inventory, mailer, audit, logger, clock) = deps;
        // Same body as before; Handle signature is now three parameters.
    }
}
```

Register the record once in `Program.cs`:

```csharp
builder.Services.AddScoped<PlaceOrder.Dependencies>();
```

## Feature-local state: use a nested service

Sometimes the state belongs to one feature only: a small cache, a compiled rule set, or a per-feature coordinator. Do not put mutable state directly on the static feature class. Put it in a nested service type and register that type with the appropriate lifetime.

```csharp
[Feature("GET /reports/{id:guid}", Summary = "Get a report")]
public static class GetReport
{
    public record Response(Guid Id, string Title, DateTimeOffset GeneratedAt);

    public sealed class Cache(IMemoryCache memory)
    {
        public async Task<Response> GetAsync(Guid id, IReportStore reports, CancellationToken ct)
        {
            var cacheKey = $"reports:{id}";
            if (memory.TryGetValue<Response>(cacheKey, out var cached))
            {
                return cached;
            }

            var report = await reports.GetAsync(id, ct).ConfigureAwait(false);
            var response = new Response(report.Id, report.Title, report.GeneratedAt);
            memory.Set(cacheKey, response, TimeSpan.FromMinutes(5));
            return response;
        }
    }

    public static Task<Response> Handle(Guid id, IReportStore reports, Cache cache, CancellationToken ct)
        => cache.GetAsync(id, reports, ct);
}
```

Register the nested service in `Program.cs`:

```csharp
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<GetReport.Cache>();
```

The `Cache` type stays in the feature file for reviewability, but the handler remains a static method with explicit parameters. Tests can still call `Handle` directly by constructing `GetReport.Cache` with a test `IMemoryCache`; if you need to replace the cache itself, depend on a small interface instead.

For caching, prefer built-in ASP.NET Core / .NET primitives before writing your own singleton dictionary:

- `IMemoryCache` for process-local cache.
- `IDistributedCache` for cache shared across app instances.
- `HybridCache` when your host stack supports it and you want a higher-level API over local and distributed cache.

## State shared across multiple features

When state is shared by several features, use a normal service type instead of nesting it under one feature:

```csharp
public interface IReportCache
{
    Task<GetReport.Response> GetAsync(Guid id, IReportStore reports, CancellationToken ct);
}

public sealed class ReportCache(IMemoryCache memory) : IReportCache
{
    public async Task<GetReport.Response> GetAsync(Guid id, IReportStore reports, CancellationToken ct)
    {
        var cacheKey = $"reports:{id}";
        if (memory.TryGetValue<GetReport.Response>(cacheKey, out var cached))
        {
            return cached;
        }

        var report = await reports.GetAsync(id, ct).ConfigureAwait(false);
        var response = new GetReport.Response(report.Id, report.Title, report.GeneratedAt);
        memory.Set(cacheKey, response, TimeSpan.FromMinutes(5));
        return response;
    }
}
```

Then inject `IReportCache` into each feature that needs it. The "one endpoint = one feature file" rule is about keeping the HTTP contract and handler together; shared infrastructure can still live behind DI when it is genuinely shared.

## Why this is *not* a Slice framework feature

The Slice philosophy is to **expand to 100% pure ASP.NET Core Minimal API**. `AddScoped<T>()` is a standard DI feature; nothing magical is added on Slice's side. That keeps three properties intact:

1. **No implicit magic** — read the code and you can see exactly what is registered.
2. **Native AOT friendliness** — the source generator emits no new behavior.
3. **Test ergonomics** — `await PlaceOrder.Handle(req, new Dependencies(mockOrders, ...), ct)` still works.

The same applies to stateful services. A DI singleton that holds an `IMemoryCache` or a thread-safe coordinator is explicit and AOT-friendly. A custom source-generator feature for instance handlers would add another construction path, make generated code less mechanical, and weaken the "plain Minimal API expansion" guarantee.

## When to apply

| Handle parameter count | Recommendation |
|---|---|
| 1–4 | Leave as is — aggregation just adds noise. |
| 5–6 | Consider it — if the count is likely to grow, aggregate now. |
| 7+ | **Aggregate.** Readability gain in code review is significant. |

For stateful services:

| Need | Recommendation |
|---|---|
| Request-local state | Use a scoped service. |
| Feature-local cache | Use a nested service and register it with the lifetime the state needs. |
| Process-wide shared state | Use a singleton service and make its internals thread-safe. |
| Shared cache across app instances | Use `IDistributedCache` or `HybridCache` when available. |
| External resource coordination | Put the coordination behind a typed DI service. |

Avoid mutable static fields on feature classes. `private static readonly ConcurrentDictionary<,>` is still shared mutable state: `readonly` protects the field reference, not the collection contents. Prefer DI so lifetimes, test isolation, and replacement in host-specific setup stay explicit. If a singleton is necessary, tests should create a fresh service provider or reset the singleton state deliberately.

## Related patterns

- [Filter configuration](filter-configuration.md) — follows the same constructor-DI idea for endpoint filters.
- [Return-type guidance](../guides/return-types.md) — when to return a plain response type vs. `IResult`.
