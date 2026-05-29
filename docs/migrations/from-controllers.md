# Migrating from ASP.NET Core controllers

Controller migration should be selective and staged. SliceFx can run in the same ASP.NET Core app as MVC controllers, so you can move one action into one feature file, compare behavior, and leave controller-heavy areas alone.

> **Preview status:** `0.1.0-preview.3` is available on NuGet. This is pre-1.0 experimental software — APIs may change before a stable release.

## When this is a good fit

Good candidates:

- API controllers whose actions already look like endpoint handlers.
- Actions with action-local request/response DTOs.
- Controllers that mostly use constructor-injected services and `CancellationToken`.
- Feature groups where route metadata, typed clients, or portability reporting would reduce drift.

Poor candidates:

- MVC apps with Razor views.
- Controllers that rely heavily on MVC-specific filters, result filters, formatter customization, or `ModelState` behavior.
- Projects centered on MediatR / `IPipelineBehavior` where the mediator pipeline is the architecture. That remains a non-goal for SliceFx.

## Stage 1: run controllers and SliceFx side by side

Keep MVC registration and add SliceFx registration:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSlice();
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();

var app = builder.Build();

app.MapControllers();
app.MapSlices();

app.Run();
```

SliceFx includes a startup migration audit for the mixed-app phase. Generated `MapSlices()` inspects the endpoints currently visible to that route builder and reports duplicate route candidates and duplicate endpoint names. Set `SliceFx:MigrationAudit` to `Off`, `Warn`, or `Throw`; the default is `Warn` in Development and `Off` outside Development.

Do not map the same route twice. For the action you migrate, remove or change the controller action route before enabling the equivalent `[Feature]` route. MVC convention routing, token replacement, and inherited attributes are best verified by the runtime audit and by a smoke request against the migrated route, not by reading the feature file alone.

## Stage 2: move one action into a feature

Before:

```csharp
[ApiController]
[Route("api/users")]
public sealed class UsersController(IUserStore store) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreateUserResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateUserResponse>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken ct)
    {
        var user = await store.AddAsync(request.Name, request.Email, ct);

        return CreatedAtAction(
            nameof(GetById),
            new { id = user.Id },
            new CreateUserResponse(user.Id, user.Name, user.Email, user.CreatedAt));
    }
}

public record CreateUserRequest(
    [Required, MinLength(2)] string Name,
    [Required, EmailAddress] string Email);

public record CreateUserResponse(Guid Id, string Name, string Email, DateTime CreatedAt);
```

After:

```csharp
using System.ComponentModel.DataAnnotations;

namespace MyApp.Features.Users;

[Feature("POST /api/users", Name = "Users.Create", Summary = "Create a new user")]
public static class CreateUser
{
    public record Request(
        [Required, MinLength(2)] string Name,
        [Required, EmailAddress] string Email);

    public record Response(Guid Id, string Name, string Email, DateTime CreatedAt);

    public static async Task<IResult> Handle(Request req, IUserStore store, CancellationToken ct)
    {
        var user = await store.AddAsync(req.Name, req.Email, ct);

        return Results.Created(
            $"/api/users/{user.Id}",
            new Response(user.Id, user.Name, user.Email, user.CreatedAt));
    }
}
```

Constructor-injected services become handler parameters. Action parameters become handler parameters or a nested `Request` record. Controller helper methods become `Results` / `TypedResults` or a direct response type.

If a `200 OK` typed response is acceptable, return `Response` directly instead of `IResult`. If status codes, headers, `Created`, `NotFound`, files, or streams are part of the contract, return `IResult` and accept that the route is `aspnet-only`. Use `FeatureAttribute.Name` when preserving the existing operation id or generated-client method name matters.

## Routing and binding

Minimal API binding remains the ASP.NET binding layer for generated Slice routes. Common binding attributes can stay on handler parameters:

```csharp
[Feature("GET /api/users/{id:guid}")]
public static class GetUser
{
    public static async Task<IResult> Handle(
        [FromRoute] Guid id,
        [FromQuery] bool includeDeleted,
        IUserStore store,
        CancellationToken ct)
    {
        var user = await store.GetAsync(id, includeDeleted, ct);
        return user is null ? Results.NotFound() : Results.Ok(user);
    }
}
```

Keep controller actions in place when they depend on MVC-only model binding, formatter behavior, or controller context APIs that do not have an obvious Minimal API equivalent.

## Filter migration

MVC filters do not translate one-for-one to Minimal API endpoint filters. Decide by behavior, not by type name:

| MVC behavior | SliceFx migration choice |
|---|---|
| Authentication, authorization, CORS, exception handling, request logging | Prefer ASP.NET Core middleware or authorization policies. Use route groups when the rule applies to all mapped slices. |
| `IActionFilter` / `IAsyncActionFilter` that validates arguments or wraps action execution | Usually converts to an `IEndpointFilter` declared with `[Filter<T>]`. |
| `IExceptionFilter` | Prefer exception-handling middleware. Endpoint filters can handle narrow feature-local cases, but they are not a global exception strategy. |
| `IResultFilter` / result mutation | Decide by behavior: JSON naming/null/default handling belongs in ASP.NET Core JSON options; headers and exception envelopes usually belong in middleware; feature-local wrapping can be an endpoint filter; API envelopes and status codes should usually be explicit response DTOs or typed results; formatter/result mutation that cannot be represented cleanly should stay on MVC for now. |
| Global MVC filters | Do not assume they run for Slice endpoints. Move cross-cutting behavior to middleware, route groups, authorization, or explicit `[Filter<T>]` declarations. |
| Attribute state on filters | Slice `[Filter<T>]` does not accept constructor values. Put configuration in DI/options or use dedicated filter types. |

See [filter declaration guidance](../guides/filter-declarations.md) and [filter configuration patterns](../patterns/filter-configuration.md).

## Validation differences to check first

`[ApiController]` and MVC validation are reflection-based and populate `ModelState`. SliceFx emits validation code for supported DataAnnotations rules and runs it before user filters.

Supported generated rules include `Required`, `StringLength`, `MinLength`, `MaxLength`, numeric `Range`, `EmailAddress`, `Url`, and `RegularExpression`. Support is shape-conditional: `StringLength` applies to `string` properties only, `Range` to numeric types only, and any attribute with a resource or localized error message counts as unsupported regardless of type. Reflection-bound validation such as custom `ValidationAttribute`, type-level validation, `IValidatableObject`, resource-based messages, or supported attribute types in unsupported shapes is reported at build time for generated ASP.NET registrations and excluded from portable WASI/Lambda function-per-feature dispatch — excluded WASI routes are simply absent from the route table, not reflection-validated. Move those rules to `ISliceValidator<TRequest>` when migrating an action.

Before migrating an action whose clients depend on validation errors, contract-test the validation response. Compare status code, `type`, `title`, `errors` keys, error messages, casing, and unsupported/custom validation behavior against the `[ApiController]` response. SliceFx returns `Results.ValidationProblem(...)` for generated validation failures, but it does not promise byte-for-byte parity with MVC `ModelState` for every formatter, localization, or custom-validation setup.

## Stage 3: migrate by feature group

After the first action passes behavior checks:

1. Pick the next action in the same route group.
2. Move action-local DTOs into the feature file when that improves reviewability; keep DTOs in a shared contracts project when generated C# clients such as Blazor clients should reference contracts without referencing server feature assemblies.
3. Keep shared domain services in DI; do not recreate controller base classes as feature base classes.
4. Run the migration audit in `Throw` mode in CI or during the PoC if route/name collisions must fail fast.
5. Run `slicefx routes` to inspect endpoint names, tags, and portability.
6. Compare hosted OpenAPI output and validation/error contract snapshots for the migrated action.
7. Delete the controller only when all of its routes have moved or the remaining routes are intentionally controller-only.

## Rollback path

A migrated feature is just a static handler plus DTOs. To roll back one server action, move the handler body and DTOs back into a controller action, remove the `[Feature]` class, and remove `MapSlices()` / `AddSlice()` when no features remain. Avoid sharing mutable state between the old controller and the new feature during a PoC.

That rollback is not automatically mechanical for the ecosystem around the endpoint. Revert generated clients, Postman collections, mock servers, OpenAPI snapshots, DTO renames, response-shape changes, and dependent branches separately if they moved with the Slice feature.
