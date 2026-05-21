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
   - Authentication only? Standard `RequireAuthorization()` is more idiomatic.
2. **Is it shared, but specific to the Minimal API filter layer?**
   - Example: per-request `Activity` start, or shape-specific endpoint filters. See the `MapGroup` option below.

## Recommended approach: `MapGroup` for common filters

`MapGroup` is **standard ASP.NET Core**, completely independent of Slice's source generator. It applies common filters to a route prefix.

### Before

```csharp
// every Users feature repeats the same filters
[Feature("GET /users/{id:guid}")]
[Filter<RequestLoggingFilter>]
[Filter<RequireApiKeyFilter>]
public static class GetUser { ... }

[Feature("POST /users")]
[Filter<RequestLoggingFilter>]
[Filter<RequireApiKeyFilter>]
public static class CreateUser { ... }

// ...18 more features
```

`Program.cs`:

```csharp
var app = builder.Build();
app.MapSlices();
app.Run();
```

### After

`MapSlices()` currently registers every feature flat against `IEndpointRouteBuilder`. In practice you have three pragmatic options:

#### Option A — Push truly global concerns into middleware

```csharp
var app = builder.Build();

// global concerns: request id, activity, CORS, etc.
app.Use(async (context, next) =>
{
    // setup
    await next();
});

app.MapSlices();
app.Run();
```

Logging, auth, CORS, rate-limiting — most "I want it everywhere" cases belong here.

#### Option B — Accept per-feature declaration as the right level of explicitness

For a real group like "every Users feature needs `RequireApiKeyFilter`", writing 20 declarations is often easier to maintain than a global injection mechanism:

- When you add feature #21, copy-paste the filter chain from an existing feature.
- When you add "a Users feature that does *not* need the API key", you simply omit the attribute — no exemption syntax needed.

#### Option C — Group declarations with `partial class`

C#'s `partial class` lets you split a feature class across files. The attributes themselves still need to live on a single declaration, so this pattern helps with code organization more than with reducing duplication:

```csharp
// Features/Users/UserFeatureFilters.cs — file is mainly for IDE navigation
namespace Slice.Sample.Features.Users;

public static partial class CreateUser { }
public static partial class GetUser { }
public static partial class DeleteUser { }
```

Practical value is limited — `[Filter<T>]` is still declared per type.

## Conclusion — is duplication a flaw or a feature?

**Mostly, it looks like a flaw but is actually a feature.**

- Truly global concerns → middleware.
- Group-level concerns → declare per feature (the duplication is the cost of being explicit).
- Remaining cases → the copy-paste maintenance cost is lower than the risk of surprise behavior from implicit injection.

"No implicit magic" is a Slice **strength**, not a missing feature. Group-scoped filter injection was considered for the framework and intentionally dropped (see the plan file `velvet-spinning-alpaca.md`).

## Related patterns

- Filter configuration: `docs/patterns/filter-configuration.md`
- Handler dependency aggregation: `docs/patterns/handler-dependencies.md`
- Filter order validation (SLICE010): apply `[FilterOrderHint(After = typeof(...))]` to a filter to surface declaration-order mistakes as a warning.
