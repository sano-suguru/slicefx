# Passing configuration to filters

`[Filter<T>]` in Slice accepts **only a type parameter**. When you need to pass a policy name, threshold, or configuration key to a filter, it is tempting to parameterize the attribute — Slice **intentionally does not** support that.

**Why**: parameterizing the attribute would push us toward a custom filter-factory mechanism, which would erode the differentiator "100% pure ASP.NET Core Minimal API expansion". Standard features already cover the use case.

## Recommended pattern: constructor DI

Filters are auto-registered as **scoped** by `AddSlice()`. Have the filter accept `IConfiguration` (or any other service) through its constructor.

### Before (hard-coded API key)

```csharp
// samples/Slice.Sample/Filters/RequireApiKeyFilter.cs
public sealed class RequireApiKeyFilter : IEndpointFilter
{
    private const string HeaderName = "X-API-Key";
    private const string ExpectedKey = "secret"; // ❌ hard-coded

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        return !context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var key) || key != ExpectedKey
            ? Results.Unauthorized()
            : await next(context).ConfigureAwait(false);
    }
}
```

### After (read via `IConfiguration`)

```csharp
public sealed class RequireApiKeyFilter : IEndpointFilter
{
    private const string HeaderName = "X-API-Key";
    private readonly string _expectedKey;

    public RequireApiKeyFilter(IConfiguration configuration)
        => _expectedKey = configuration["Auth:ApiKey"]
            ?? throw new InvalidOperationException("Auth:ApiKey configuration is missing.");

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        return !context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var key)
            || !StringComparer.Ordinal.Equals(key.ToString(), _expectedKey)
            ? Results.Unauthorized()
            : await next(context).ConfigureAwait(false);
    }
}
```

`appsettings.json`:

```json
{
  "Auth": {
    "ApiKey": "secret"
  }
}
```

The literal value is illustrative. In production, load secrets from environment variables,
user-secrets, or a dedicated secret store rather than committing them to `appsettings.json`.

The feature declaration is unchanged:

```csharp
[Filter<RequireApiKeyFilter>]
public static class DeleteUser { ... }
```

## Multiple policies via dedicated filter types

When you have multiple policies (admin, read-only, etc.), pair the constructor pattern with `IOptions<T>`:

```csharp
public sealed record AuthPolicyOptions(string ApiKey, string HeaderName = "X-API-Key");

public abstract class RequireApiKeyFilterBase : IEndpointFilter
{
    private readonly AuthPolicyOptions _options;
    protected RequireApiKeyFilterBase(AuthPolicyOptions options) => _options = options;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        return !context.HttpContext.Request.Headers.TryGetValue(_options.HeaderName, out var key)
            || !StringComparer.Ordinal.Equals(key.ToString(), _options.ApiKey)
            ? Results.Unauthorized()
            : await next(context).ConfigureAwait(false);
    }
}

public sealed class RequireAdminKeyFilter(IConfiguration cfg)
    : RequireApiKeyFilterBase(new(cfg["Auth:AdminKey"]!));

public sealed class RequireReadOnlyKeyFilter(IConfiguration cfg)
    : RequireApiKeyFilterBase(new(cfg["Auth:ReadOnlyKey"]!));
```

Pick the right one per feature:

```csharp
[Feature("DELETE /users/{id:guid}")]
[Filter<RequireAdminKeyFilter>]
public static class DeleteUser { ... }

[Feature("GET /reports")]
[Filter<RequireReadOnlyKeyFilter>]
public static class GetReports { ... }
```

## Why filter attributes are not parameterized

| Option | Consequence |
|---|---|
| `[Filter<X>(ConfigSection = "Auth:Admin")]` source-generator extension | Introduces a custom factory mechanism — erodes the differentiator. |
| `[Filter<X>(Args = new object[] {...})]` | `object[]` capture is hostile to Native AOT. |
| **Constructor DI** (recommended) | Standard feature; Slice needs no changes. |

## Related patterns

- Handler dependency aggregation: `docs/patterns/handler-dependencies.md`
- Return-type guidance: `docs/guides/return-types.md`
- Filter declaration duplication: `docs/guides/filter-declarations.md`
