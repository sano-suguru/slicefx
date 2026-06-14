# AOT-safe scoped services and cross-cutting concerns

SliceFx runs on ASP.NET Core NativeAOT, WASI via componentize-dotnet, and Lambda NativeAOT custom
runtime. Each hosting path has different trimming characteristics. This guide shows how to register
scoped services safely and wire up cross-cutting concerns via `ISliceFilter`.

## Registration: when are factory lambdas needed?

**ASP.NET NativeAOT** (the primary path): `Microsoft.Extensions.DependencyInjection` is
AOT-annotated, so standard type-based registration is safe without factory lambdas:

```csharp
// ✓ AOT-safe on ASP.NET NativeAOT — no IL3050
builder.Services.AddScoped<ICurrentWorkspace, CurrentWorkspace>();
```

Only add a factory lambda if the compiler emits `IL3050` for a specific registration. Prefer the
simple form above as the default.

**WASI / Lambda NativeAOT-LLVM** (full-trim paths): `ActivatorUtilities` may use reflection for
parameterised constructors under full trim. For those hosts, prefer an explicit factory lambda:

```csharp
// ✓ Safe under full-trim NativeAOT-LLVM
builder.Services.AddScoped<ICurrentWorkspace>(_ => new CurrentWorkspace());
```

## Canonical pattern: authentication via `[SliceFilter<T>]` + scoped context

A common cross-cutting concern is authentication: read a token from the request, validate it, and
make the resolved identity available to every handler in the same request scope.

### 1. Define the scoped context (mutable)

The context **must be mutable** so the filter can write the resolved identity back into it. Handlers
then read the populated value directly from DI.

```csharp
public interface ICurrentWorkspace
{
    string? WorkspaceId { get; set; }
}

// Mutable scoped holder — populated by WorkspaceAuthFilter before the handler runs.
public sealed class CurrentWorkspace : ICurrentWorkspace
{
    public string? WorkspaceId { get; set; }
}
```

### 2. Implement the filter

`ISliceFilter.InvokeAsync` takes **two parameters** (`SliceFilterContext`, `SliceFilterDelegate`).
The cancellation token is available as `context.CancellationToken`.

```csharp
// Fail-closed: if authentication fails, short-circuits with 401 before the handler runs.
public sealed class WorkspaceAuthFilter(IAuthenticator auth) : ISliceFilter
{
    public async ValueTask<SliceFilterResult> InvokeAsync(
        SliceFilterContext context,
        SliceFilterDelegate next)
    {
        context.Headers.TryGetValue("Authorization", out var token);

        var workspaceId = await auth.ResolveAsync(token, context.CancellationToken)
                                    .ConfigureAwait(false);
        if (workspaceId is null)
        {
            return SliceFilterResult.ShortCircuit(SliceResult.Unauthorized("Invalid or missing token."));
        }

        // Write the resolved identity into the scoped holder so handlers can inject it directly.
        var current = context.Services.GetRequiredService<ICurrentWorkspace>();
        current.WorkspaceId = workspaceId;

        return await next(context).ConfigureAwait(false);
    }
}
```

### 3. Register everything

```csharp
// In Program.cs:
builder.Services.AddScoped<IAuthenticator, MyAuthenticator>();

// Register the interface → concrete class (AOT-safe on ASP.NET NativeAOT).
// For WASI/NativeAOT-LLVM full-trim, use a factory lambda instead:
//   builder.Services.AddScoped<ICurrentWorkspace>(_ => new CurrentWorkspace());
builder.Services.AddScoped<ICurrentWorkspace, CurrentWorkspace>();
```

### 4. Decorate protected features

Use `[SliceFilter<T>]` (not `[Filter<T>]`) for host-neutral filters that should run on both
ASP.NET and WASI paths.

```csharp
[Feature("GET /items/{id}")]
[SliceFilter<WorkspaceAuthFilter>]    // ← [SliceFilter] for ISliceFilter; [Filter] is for IEndpointFilter
public static class GetItem
{
    public static Task<SliceResult<ItemResponse>> Handle(
        string id,
        ICurrentWorkspace ws,         // ← injected from the scoped context; always populated here
        IItemStore store,
        CancellationToken ct)
        => store.GetAsync(ws.WorkspaceId!, id, ct);
}
```

> **`[Filter<T>]` vs `[SliceFilter<T>]`**
> - `[SliceFilter<T>]` declares a host-neutral `ISliceFilter` — runs on ASP.NET *and* WASI.
> - `[Filter<T>]` declares an ASP.NET-only `IEndpointFilter` — not executed in WASI dispatch.
>
> For cross-cutting concerns like authentication that must run on both paths, always use
> `[SliceFilter<T>]`.

### 5. Fail-closed discipline

Adding `[SliceFilter<WorkspaceAuthFilter>]` is **opt-in per feature**, which means a missing
attribute means fail-open (unauthenticated access). Prevent accidental omissions by:

- Treating the attribute as **mandatory on every non-public feature** (code-review checklist).
- Writing a manifest-based test that walks all registered `SliceRouteDescriptor` instances and
  asserts that every route not on an explicit allowlist carries the filter. (Reading the manifest
  record uses generated code, not per-request reflection, so it is AOT-safe.)

```csharp
// Example meta-test (xUnit, runs in CI)
[Fact]
public void All_protected_routes_have_WorkspaceAuthFilter()
{
    var publicRoutes = new HashSet<string>(StringComparer.Ordinal)
    {
        "POST /workspaces",
        "GET /shares/{token}",
    };

    var routes = MyApp.GetSliceRoutesGenerated(); // source-generated method
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
