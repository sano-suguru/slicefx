# Migrating from plain Minimal APIs

SliceFx is designed to sit beside standard ASP.NET Core Minimal APIs. You can migrate one endpoint at a time, keep the rest of the app unchanged, and stop the migration if the feature shape does not pay for itself.

> **Preview status:** `0.1.0-preview.1` is available on NuGet. This is pre-1.0 experimental software — APIs may change before a stable release.

## When this is a good fit

Use this path when an existing Minimal API app has endpoint code, DTOs, validation, route names, and client wiring that are starting to drift apart. It is especially useful for small-to-medium APIs where you can prove the shape with one feature group before touching the rest of the app.

Do not migrate an endpoint just to make it "portable". Endpoints that intentionally use ASP.NET-only behavior can stay as raw Minimal APIs or become Slice features that return `IResult` and are classified as `aspnet-only`.

## Stage 1: add SliceFx beside existing endpoints

Keep your existing builder style. `CreateSlimBuilder` is used in samples because the framework is trimming/AOT-oriented, but a migrated app does not need to switch builders just to try SliceFx.

Add the packages:

```bash
dotnet add package SliceFx.Core --version 0.1.0-preview.1
dotnet add package SliceFx.SourceGenerator --version 0.1.0-preview.1
```

Then add a global using for the generated namespace:

```xml
<ItemGroup>
  <Using Include="SliceFx" />
</ItemGroup>
```

Then register Slice services and map generated routes beside your existing `MapGet` / `MapPost` calls:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSlice();
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("ok")); // unchanged
app.MapSlices();                               // generated Slice endpoints

app.Run();
```

SliceFx includes two migration safety nets, but neither should replace a route inventory:

- The source generator reports warnings for simple same-project literal Minimal API overlaps such as `app.MapPost("/users", ...)` beside `[Feature("POST /users")]`, and for literal `.WithName("...")` values that match generated Slice endpoint names.
- Generated `MapSlices()` runs a startup migration audit over the endpoints currently visible to that route builder. Set `SliceFx:MigrationAudit` to `Off`, `Warn`, or `Throw`; the default is `Warn` in Development and `Off` outside Development.

Avoid duplicate routes while migrating. If a feature maps `POST /users`, remove or disable the old `app.MapPost("/users", ...)` registration before running both in the same app. Dynamic routes, helper methods, and convention-heavy mapping may only be caught by the startup audit or by ASP.NET Core routing when the app starts.

## Stage 2: move one endpoint

Start with a low-risk endpoint that has a clear request/response shape.

Before:

```csharp
app.MapPost("/users", async (
    CreateUserRequest req,
    IUserStore store,
    CancellationToken ct) =>
{
    var user = await store.AddAsync(req.Name, req.Email, ct);
    return Results.Created($"/users/{user.Id}", new CreateUserResponse(
        user.Id,
        user.Name,
        user.Email,
        user.CreatedAt));
})
.WithName("Users.CreateUser")
.WithTags("Users")
.WithSummary("Create a new user");

public record CreateUserRequest(
    [Required, MinLength(2)] string Name,
    [Required, EmailAddress] string Email);

public record CreateUserResponse(Guid Id, string Name, string Email, DateTime CreatedAt);
```

After:

```csharp
using System.ComponentModel.DataAnnotations;

namespace MyApp.Features.Users;

[Feature("POST /users", Name = "Users.CreateUser", Summary = "Create a new user")]
public static class CreateUser
{
    public record Request(
        [Required, MinLength(2)] string Name,
        [Required, EmailAddress] string Email);

    public record Response(Guid Id, string Name, string Email, DateTime CreatedAt);

    public static async Task<Response> Handle(Request req, IUserStore store, CancellationToken ct)
    {
        var user = await store.AddAsync(req.Name, req.Email, ct);
        return new Response(user.Id, user.Name, user.Email, user.CreatedAt);
    }
}
```

This version returns `200 OK` with a typed response because the handler returns `Response` directly. If preserving `201 Created` is more important than portability, keep the HTTP-shaped behavior:

```csharp
public static async Task<IResult> Handle(Request req, IUserStore store, CancellationToken ct)
{
    var user = await store.AddAsync(req.Name, req.Email, ct);
    return Results.Created($"/users/{user.Id}", new Response(user.Id, user.Name, user.Email, user.CreatedAt));
}
```

That endpoint remains a normal ASP.NET Core endpoint, but the current generator classifies any `IResult` or `TypedResults` return as `aspnet-only`. The `partial` portability status is reserved for routes whose shape is portable while attached behavior, such as endpoint filters or reflection-bound validation, is not. See [return-type guidance](../guides/return-types.md).

## Stage 3: preserve filters, groups, and metadata deliberately

Generated Slice registrations call standard Minimal API methods: `MapMethods`, generated validation filters, declared `[Filter<T>]` filters, then tags, endpoint name, and summary.

Use the same ASP.NET layers you already use:

| Existing Minimal API shape | Migration choice |
|---|---|
| Middleware such as logging, CORS, authentication, exception handling | Leave it in middleware. |
| Route-group prefix or shared policy | Map slices through a group: `app.MapGroup("/api").RequireAuthorization().MapSlices();`. Route groups scope endpoint metadata and conventions such as auth, CORS, rate limiting, output caching, and endpoint filters; they do not create a separate middleware pipeline. Middleware order is still ASP.NET pipeline order, so do not expect middleware placed between `MapPost` and `MapSlices` calls to scope only one endpoint set. See [filter declaration guidance](../guides/filter-declarations.md) and [filter configuration patterns](../patterns/filter-configuration.md). |
| `RequireAuthorization()` or fallback authorization | Prefer ASP.NET Core authorization policies on middleware or route groups. Do not use `[Filter<T>]` as a replacement for the authorization system. |
| `[Authorize]` on a handler or endpoint | Prefer group-level `RequireAuthorization(...)` during migration. If you rely on attributes, verify the runtime OpenAPI and authorization metadata before deleting the raw endpoint. |
| `HttpContext`, `ClaimsPrincipal`, remote IP, headers, route/query/body binding attributes | Keep them as handler parameters when Minimal API binding supports them; contract-test behavior before moving code that depends on ambient `HttpContext.Items` state. |
| `[AsParameters]` query/route aggregation | Keep the same parameter object only if Minimal API binding still accepts it in the generated delegate; otherwise split parameters or keep the raw endpoint. |
| Per-endpoint endpoint filter | Convert to a plain `IEndpointFilter` and declare `[Filter<YourFilter>]` on the feature. |
| Route name, tag, summary | Use `FeatureAttribute.Name`, `Tag`, and `Summary`. Endpoint names default to `{Tag}.{FeatureClassName}`, but `Name` preserves an existing operation id/client method deliberately. |
| `CacheOutput()`, `RequireRateLimiting()`, custom `Accepts<T>()` / `Produces<T>()`, custom `RouteHandlerBuilder` metadata | Keep the endpoint raw until you have an explicit route-group policy, typed-result return, or other replacement. SliceFx does not mirror every builder extension as attributes. |
| Form or file upload (`IFormFile`, multipart) | Keep raw when exact binding, antiforgery, request-size, or OpenAPI shape matters. |
| Custom status code, headers, redirects, files, streams | Return `IResult` and accept `aspnet-only`, or keep the endpoint as raw Minimal API. |

Feature filters are intentionally explicit. SliceFx does not inject filters that are not declared in source. See [filter declarations](../guides/filter-declarations.md) and [filter configuration](../patterns/filter-configuration.md).

## Stage 4: use tooling after the first slice

Once at least one endpoint is a feature, use the generated route manifest to see what you have:

```bash
slicefx routes
slicefx routes --format json
```

Portable and partial routes can feed generated clients:

```bash
slicefx client csharp --output SliceApiClient.g.cs
slicefx client typescript --output slice-api-client.ts
```

The C# client reuses C# contract types from handler signatures. Nested feature DTOs keep endpoint code local, while non-nested DTOs in a shared contracts project are the better fit when Blazor or another .NET client should consume generated clients without referencing server feature assemblies.

For hosted ASP.NET Core OpenAPI, keep using Microsoft's runtime OpenAPI support. The CLI OpenAPI output is a manifest projection for portable tooling, not a replacement for the hosted document. See [OpenAPI guidance](../guides/openapi.md).

Compare the hosted OpenAPI document before and after each migrated endpoint when clients depend on operation ids, status codes, request content types, response schemas, auth metadata, cache/rate-limit metadata, or examples. `FeatureAttribute.Name` helps preserve operation ids, but response metadata still follows the handler return type and ASP.NET Core metadata.

## Known migration limits

Keep an endpoint as raw Minimal API until you have an explicit replacement when it relies on:

- Complex `RouteHandlerBuilder` chains that SliceFx does not emit today, such as custom metadata conventions beyond name, tag, and summary.
- Form or file upload behavior where the exact binding and OpenAPI shape matters.
- Custom binding that works in ASP.NET but is not meaningful to the route manifest, typed-client generation, WASI, or function-per-feature Lambda.
- Endpoint filters whose ordering depends on route-group behavior you have not replicated.
- Authorization, output caching, rate limiting, or OpenAPI metadata that exists only as endpoint-builder calls and has no replacement route group or return-type shape yet.
- HTTP result unions, custom headers, streaming, or non-JSON responses where `IResult` is the clearer contract.

These are not failures. Mixing raw Minimal APIs and Slice features is the intended migration mode.

## Rollback path

SliceFx's generated registrations are standard Minimal API registrations. If a proof of concept does not help, move the `Handle` body back into `MapGet` / `MapPost`, remove the `[Feature]` class, and remove `AddSlice()` / `MapSlices()` when no features remain.

That rollback is mechanical only for the server endpoint code. If the migration also changed generated clients, Postman collections, mock servers, OpenAPI snapshots, DTO names/namespaces, response shapes, or dependent branches, roll those artifacts back explicitly before calling the rollback complete.
