# AOT-safe scoped services and cross-cutting concerns

SliceFx runs on full-trim NativeAOT hosts (WASI via componentize-dotnet, Lambda NativeAOT custom
runtime). Reflection-based activation used by the default DI container for concrete types can cause
trimming issues at publish time. This guide shows how to register scoped services in a way that is
safe under all three hosting paths.

## The problem with default activation

When you register a concrete type with only `AddScoped<MyService>()`, the default
`IServiceProvider` activates it via `ActivatorUtilities`, which may use reflection to locate
and call the constructor. Under full-trim NativeAOT this reflection call may fail or emit a warning.

## The solution: factory-lambda registration

Register every scoped (or transient) concrete type with an explicit **factory lambda** that calls
`new` directly. The DI container never needs to reflect on the type:

```csharp
// ✓ AOT-safe — the constructor call is compiled as a direct call
builder.Services.AddScoped<CurrentWorkspace>(sp =>
{
    // Resolve dependencies explicitly; no reflection, no hidden activation
    var auth = sp.GetRequiredService<IAuthenticator>();
    return new CurrentWorkspace(auth);
});
```

Singleton services are unaffected because they are typically activated once at startup where
startup-time reflection does not impact the hot path. But for completeness, prefer factory lambdas
for singletons too when possible.

## Canonical pattern: authentication via `[SliceFilter<T>]` + scoped context

A common cross-cutting concern is authentication: read a token from the request, validate it, and
make the resolved identity available to every handler that needs it.

### 1. Define the scoped context

```csharp
// Stores the resolved workspace for the current request.
public sealed class CurrentWorkspace
{
    public string WorkspaceId { get; }
    public CurrentWorkspace(string workspaceId) => WorkspaceId = workspaceId;
}
```

### 2. Implement the filter

```csharp
// Fail-closed: if authentication fails, short-circuit with 401. The resolved workspace
// is stored in the scoped CurrentWorkspace service so handlers can inject it directly.
public sealed class WorkspaceAuthFilter : ISliceFilter
{
    private readonly IAuthenticator _auth;
    private readonly CurrentWorkspace? _workspace; // null until resolved

    // ISliceFilter receives its own dependencies from DI.
    public WorkspaceAuthFilter(IAuthenticator auth)
        => _auth = auth;

    public async ValueTask<SliceFilterResult> InvokeAsync(
        SliceFilterContext ctx,
        SliceFilterDelegate next,
        CancellationToken ct)
    {
        var token = ctx.Headers.TryGetValue("Authorization", out var h) ? h : null;
        var ws = await _auth.ResolveAsync(token, ct);
        if (ws is null)
            return SliceFilterResult.ShortCircuit(SliceResult.Unauthorized("Invalid or missing token."));

        // Store the resolved workspace in the scoped CurrentWorkspace service.
        // Handlers that inject CurrentWorkspace will receive this instance.
        var scoped = ctx.Services.GetRequiredService<CurrentWorkspace>();
        // (see note below on mutable context pattern)
        return await next(ctx);
    }
}
```

> **Mutable context note**: the pattern above works best when `CurrentWorkspace` is mutable or
> when the filter stores the resolved value via a setter/init. Alternatively, use
> `IHttpContextAccessor`-style keyed storage via `ctx.Services` to thread the value through to
> handlers.

### 3. Register everything with factory lambdas

```csharp
// In Program.cs / WasiHost setup:

// IAuthenticator — inject your real implementation (e.g. a HMAC verifier or DB lookup).
builder.Services.AddScoped<IAuthenticator, MyAuthenticator>();

// CurrentWorkspace is a request-scoped holder; register with a factory lambda (AOT-safe).
// The filter populates it; handlers read from it.
builder.Services.AddScoped<CurrentWorkspace>(_ => new CurrentWorkspace(string.Empty));
```

### 4. Decorate protected features

```csharp
[Feature("GET /items/{id}")]
[Filter<WorkspaceAuthFilter>]          // ← fail-closed: no filter = no access
public static class GetItem
{
    public static Task<SliceResult<ItemResponse>> Handle(
        string id,
        CurrentWorkspace ws,           // ← injected from scoped context
        IItemStore store,
        CancellationToken ct)
        => store.GetAsync(ws.WorkspaceId, id, ct);
}
```

### 5. Fail-closed discipline

Adding `[Filter<WorkspaceAuthFilter>]` is **opt-in per feature**, which means a missing attribute
means fail-open (unauthenticated access). Prevent accidental omissions by:

- Treating the attribute as **mandatory on every non-public feature** (code-review checklist).
- Writing a manifest-based test that walks all registered `SliceRouteDescriptor` instances and
  asserts that every route not on an explicit allowlist carries the filter. (Reflection on the
  manifest record is generated code, not per-request reflection, so it is safe.)

```csharp
// Example meta-test (MSTest / xUnit, runs in CI)
[Fact]
public void All_protected_routes_have_WorkspaceAuthFilter()
{
    var publicRoutes = new HashSet<string>(StringComparer.Ordinal)
    {
        "POST /workspaces",
        "GET /shares/{token}",
    };

    var routes = MyApp.GetSliceRoutesGenerated(); // generated method
    foreach (var route in routes)
    {
        if (publicRoutes.Contains(route.Route))
            continue;

        Assert.Contains(
            typeof(WorkspaceAuthFilter).FullName,
            route.SliceFilterTypeNames,
            StringComparer.Ordinal);
    }
}
```

## WASI path note

`[Filter<T>]` endpoint filters **are not executed in the WASI path**. They require ASP.NET's
`IEndpointFilter` pipeline. For WASI, use `ISliceValidator<T>` for request validation and
`ISliceFilter` for cross-cutting logic that must run in-process. Both are discovered by the source
generator and executed in generated WASI dispatch.

See also: [return-types.md](../guides/return-types.md) for `SliceResult` error paths.
