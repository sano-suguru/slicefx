# Filter declaration duplication and how to handle it

Slice asks each feature file to declare its own filters: `[Filter<RequestLoggingFilter>]` is written once per feature. If you want a logging filter on every endpoint in a `Users` feature group of 20, you write it 20 times.

This document explains whether that is a flaw or a feature, and shows **standard-feature workarounds** for the cases where it genuinely hurts.

## What Slice deliberately does not do

Slice has **no mechanism to inject filters that are not declared in source**. That is one of the strength-preservation principles ("no implicit magic").

Benefits:

- **PR diffs show every filter in every feature's chain** — there are no surprise filters running behind the scenes.
- **Opting out of a "shared" filter is trivial** — just remove the declaration; no global-filter exemption syntax to remember.
- **`slice routes --format json` accurately describes the live filter chain** — tooling stays trustworthy.

## Question the premise first

If 20 features declare the same filter, ask:

1. **Is this filter truly required on every endpoint?**
   - Logging only? An ASP.NET Core middleware (e.g. `app.UseSerilogRequestLogging()`) is the right layer.
   - Authorization only? ASP.NET Core Authorization middleware, fallback policies, or `RequireAuthorization()` on a route group are more idiomatic.
2. **Is it shared, but specific to the Minimal API filter layer?**
   - Example: per-request `Activity` start, or shape-specific endpoint filters. See the `MapGroup` option below.

## Pragmatic options when filters repeat

Slice has no mechanism to inject filters into features that did not declare them. When the same `[Filter<T>]` appears in many features, you have three options — pick by scope.

### Option A — Push truly global concerns into middleware

Logging, request id, CORS, rate limiting, auth scheme negotiation — most "I want this everywhere" cases belong in ASP.NET Core middleware, not in the endpoint-filter pipeline.

```csharp
var app = builder.Build();

app.UseSerilogRequestLogging();   // request logging — middleware, not [Filter<T>]
app.UseAuthentication();
app.UseAuthorization();

app.MapSlices();
app.Run();
```

This is the right layer for cross-cutting concerns that do not need access to the endpoint metadata or the typed `Request`.

### Option B — Attach a shared endpoint filter or policy to all Slice routes via `MapGroup`

`MapSlices()` is an `IEndpointRouteBuilder` extension, so it composes with `MapGroup`. Use this when the concern is genuinely an endpoint filter (needs the endpoint pipeline) but applies to every Slice route in the host.

```csharp
var app = builder.Build();

var slices = app.MapGroup("").RequireAuthorization("Admin");
slices.MapSlices();   // every feature inherits the policy
```

See [`filter-configuration.md`](../patterns/filter-configuration.md#when-the-concern-is-authorization) for the working authorization example, and the closed-generic-filter alternative when several filters share logic but differ by a policy marker.

Path prefixes (`MapGroup("/api")`) will *not* prefix Slice features — `[Feature("GET /users/{id}")]` patterns are absolute. `MapGroup` is useful here for metadata aggregation (filters, policies, tags), not for routing.

### Option C — Accept per-feature declaration as the right level of explicitness

When the duplication is real but the concern is genuinely feature-scoped (e.g., "every Users feature needs `RequireApiKeyFilter`"), 20 explicit declarations are often easier to maintain than any indirection. Adding feature #21 is one copy-paste; opting one feature out is one line deleted; tooling like `slice routes --format json` continues to describe the live chain accurately.

## Conclusion — is duplication a flaw or a feature?

**Mostly, it looks like a flaw but is actually a feature.**

- Truly global concerns → middleware.
- Authorization concerns → ASP.NET Core Authorization.
- Group-level endpoint-filter concerns → declare per feature (the duplication is the cost of being explicit) or use a route group when the grouping is real.
- Repeated endpoint-filter variants with identical filter logic → use [closed generic policy filters](../patterns/filter-configuration.md#repeated-endpoint-filter-variants-closed-generic-policy-filters) such as `[Filter<AuditFilter<AdminAuditPolicy>>]`.
- Remaining cases → the copy-paste maintenance cost is lower than the risk of surprise behavior from implicit injection.

"No implicit magic" is a Slice **strength**, not a missing feature. Filter classes are infrastructure types, not feature files, so a small number of shared filters or typed policy markers does not violate the "one endpoint = one feature file" model. Group-scoped filter injection was considered for the framework and intentionally dropped.

## Related patterns

- [Filter configuration](../patterns/filter-configuration.md) — closed generic policy filters, `IOptions<T>` configuration recipes.
- [Handler dependency and state patterns](../patterns/handler-dependencies.md) — grouping dependencies and putting feature state behind DI.
- Filter order validation (SLICE010): apply `[FilterOrderHint(After = typeof(...))]` to a filter to surface declaration-order mistakes as a warning.
