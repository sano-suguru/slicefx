# Handler dependency aggregation pattern

A Slice `Handle` method has no constructor injection (it is a static method). Every dependency arrives as a method parameter. That is fine for simple endpoints but starts to feel noisy once a complex business handler needs five or more dependencies.

This document shows how to aggregate dependencies **without changing Slice itself** — using only standard C# and DI features.

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

## Why this is *not* a Slice framework feature

The Slice philosophy is to **expand to 100% pure ASP.NET Core Minimal API**. `AddScoped<T>()` is a standard DI feature; nothing magical is added on Slice's side. That keeps three properties intact:

1. **No implicit magic** — read the code and you can see exactly what is registered.
2. **Native AOT friendliness** — the source generator emits no new behavior.
3. **Test ergonomics** — `await PlaceOrder.Handle(req, new Dependencies(mockOrders, ...), ct)` still works.

## When to apply

| Handle parameter count | Recommendation |
|---|---|
| 1–4 | Leave as is — aggregation just adds noise. |
| 5–6 | Consider it — if the count is likely to grow, aggregate now. |
| 7+ | **Aggregate.** Readability gain in code review is significant. |

## Related patterns

- Filter configuration follows the same constructor-DI idea: see `docs/patterns/filter-configuration.md`.
- Return-type guidance: `docs/guides/return-types.md`.
