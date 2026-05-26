# Passing configuration to filters

`[Filter<T>]` in Slice accepts **only a type parameter**. You can compose multiple filters with multiple attributes, but you cannot pass per-feature values through the attribute itself.

**Why**: parameterizing the attribute would push Slice toward a custom filter-factory mechanism, which would erode the differentiator "100% pure ASP.NET Core Minimal API expansion". Standard ASP.NET Core features already cover the use case without adding Slice-specific runtime state.

## Recommended pattern: constructor DI

Filters are auto-registered as **scoped** by `AddSlice()`. Have the filter accept `IConfiguration`, `IOptions<T>`, or any other service through its constructor.

### Before (hard-coded API key)

```csharp
// samples/SliceFx.Sample/Filters/RequireApiKeyFilter.cs
public sealed class RequireApiKeyFilter : IEndpointFilter
{
    private const string HeaderName = "X-API-Key";
    private const string ExpectedKey = "secret"; // demo only

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

The literal value is illustrative. In production, load secrets from environment variables, user-secrets, or a dedicated secret store rather than committing them to `appsettings.json`.

The feature declaration is unchanged:

```csharp
[Filter<RequireApiKeyFilter>]
public static class DeleteUser { ... }
```

## When the concern is authorization

If the policy is truly an authorization policy, prefer ASP.NET Core Authorization instead of a Slice endpoint filter. It is the standard layer for named auth policies, role checks, fallback policies, and auth middleware integration.

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole("Admin"));

var app = builder.Build();
var admin = app.MapGroup("").RequireAuthorization("Admin"); // no path prefix; metadata only
admin.MapSlices();
```

Use a real prefix when you want one: `app.MapGroup("/api").MapSlices()` maps `[Feature("GET /users/{id}")]` as `GET /api/users/{id}`.

Use Slice filters for feature-local endpoint filter behavior that does not belong in the authorization system. The API-key examples below are intentionally demo-only; they show configuration mechanics, not a replacement for ASP.NET Core Authorization.

## Simple variants: dedicated filter types

For one or two variants, a small dedicated filter type is often the clearest option.

```csharp
public abstract class RequireApiKeyFilterBase : IEndpointFilter
{
    private readonly string _apiKey;

    protected RequireApiKeyFilterBase(string apiKey) => _apiKey = apiKey;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        return !context.HttpContext.Request.Headers.TryGetValue("X-API-Key", out var key)
            || !StringComparer.Ordinal.Equals(key.ToString(), _apiKey)
            ? Results.Unauthorized()
            : await next(context).ConfigureAwait(false);
    }
}

public sealed class RequireAdminKeyFilter(IConfiguration cfg)
    : RequireApiKeyFilterBase(cfg["Auth:AdminKey"]
        ?? throw new InvalidOperationException("Auth:AdminKey configuration is missing."));
```

Pick the concrete type per feature:

```csharp
[Feature("DELETE /users/{id:guid}")]
[Filter<RequireAdminKeyFilter>]
public static class DeleteUser { ... }
```

## Repeated endpoint-filter variants: closed generic policy filters

When several policies share identical filter logic, use a closed generic filter. The policy marker is the type-level argument, so the feature still declares an explicit filter chain without stringly-typed attribute values.

The API-key example remains illustrative. If you use the pattern for a secret-backed demo or internal tool, fail fast on missing or empty configuration and load real secrets from environment variables, user-secrets, or a dedicated secret store.

```csharp
public interface IApiKeyPolicy
{
    static abstract string Name { get; }
}

public sealed class AdminPolicy : IApiKeyPolicy
{
    public static string Name => "Admin";
}

public sealed class ReadOnlyPolicy : IApiKeyPolicy
{
    public static string Name => "ReadOnly";
}

public sealed record ApiKeyPolicyOptions
{
    public required string ApiKey { get; init; }
    public string HeaderName { get; init; } = "X-API-Key";
}

public sealed class RequireApiKeyFilter<TPolicy>(
    IOptionsMonitor<ApiKeyPolicyOptions> policies) : IEndpointFilter
    where TPolicy : IApiKeyPolicy
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var options = policies.Get(TPolicy.Name);
        return !context.HttpContext.Request.Headers.TryGetValue(options.HeaderName, out var key)
            || !StringComparer.Ordinal.Equals(key.ToString(), options.ApiKey)
            ? Results.Unauthorized()
            : await next(context).ConfigureAwait(false);
    }
}
```

Register named options in normal ASP.NET Core DI and validate them at startup:

```csharp
builder.Services.AddOptions<ApiKeyPolicyOptions>(AdminPolicy.Name)
    .Bind(builder.Configuration.GetRequiredSection("Auth:Admin"))
    .Validate(static options => !string.IsNullOrWhiteSpace(options.ApiKey), "API key is required.")
    .Validate(static options => !string.IsNullOrWhiteSpace(options.HeaderName), "Header name is required.")
    .ValidateOnStart();

builder.Services.AddOptions<ApiKeyPolicyOptions>(ReadOnlyPolicy.Name)
    .Bind(builder.Configuration.GetRequiredSection("Auth:ReadOnly"))
    .Validate(static options => !string.IsNullOrWhiteSpace(options.ApiKey), "API key is required.")
    .Validate(static options => !string.IsNullOrWhiteSpace(options.HeaderName), "Header name is required.")
    .ValidateOnStart();
```

Pick the closed filter type per feature:

```csharp
[Feature("DELETE /users/{id:guid}")]
[Filter<RequireApiKeyFilter<AdminPolicy>>]
public static class DeleteUser { ... }

[Feature("GET /reports")]
[Filter<RequireApiKeyFilter<ReadOnlyPolicy>>]
public static class GetReports { ... }
```

Slice registers each closed filter type it sees, for example `RequireApiKeyFilter<AdminPolicy>` and `RequireApiKeyFilter<ReadOnlyPolicy>`. Do not also register the open generic manually unless you have a separate non-Slice use case.

`FilterOrderHint` also uses exact filter type identity. If the order constraint is policy-specific, target the same closed type:

```csharp
[FilterOrderHint(After = typeof(RequireApiKeyFilter<AdminPolicy>))]
public sealed class AuditFilter : IEndpointFilter { ... }
```

## Why filter attributes are not parameterized

| Option | Consequence |
|---|---|
| `[Filter<X>(ConfigSection = "Auth:Admin")]` source-generator extension | Introduces a custom factory mechanism and hides behavior in attribute state. |
| `[Filter<X>(Args = new object[] {...})]` | `object[]` capture is hostile to Native AOT and type safety. |
| **Constructor DI / closed generic filter types** | Standard C# and ASP.NET Core DI; Slice needs no custom runtime factory. |

## Related patterns

- [Handler dependency and state patterns](handler-dependencies.md) — grouping dependencies and putting feature state behind DI for a static `Handle`.
- [Return-type guidance](../guides/return-types.md) — when to return a plain response type vs. `IResult`.
- [Filter declaration duplication](../guides/filter-declarations.md) — why `[Filter<T>]` is declared per feature, and how to keep the duplication manageable.
