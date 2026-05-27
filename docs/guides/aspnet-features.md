# ASP.NET features and escape hatches

SliceFx generated code is pure Minimal API expansion. Every feature endpoint is a standard
`WebApplication.MapMethods` call — the full ASP.NET Core surface stays available, and nothing in
the framework restricts which ASP.NET features you can use.

## What you keep

These features work on SliceFx feature endpoints without any special configuration:

| Feature | How to use |
| --- | --- |
| ASP.NET Core Authorization | `[Authorize]`, policies, `RequireAuthorization()`, fallback policy |
| Output caching | `CacheOutput()` via route group |
| Rate limiting | `RequireRateLimiting()` via route group |
| CORS | `RequireCors()` via route group |
| Exception handling | Exception handling middleware and `IProblemDetailsService` |
| Endpoint filters | `[Filter<T>]` applies standard `IEndpointFilter` in declaration order |
| OpenAPI | `builder.Services.AddOpenApi()` / `app.MapOpenApi()` |
| Standard binding | `[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromForm]`, `[FromServices]`, `[FromKeyedServices]` |

## Route groups for cross-cutting policies

Route groups scope ASP.NET endpoint metadata and conventions without creating a separate middleware
pipeline. Policies applied to the group apply to every slice mapped through it:

```csharp
app.MapGroup("/api")
   .RequireAuthorization("AdminPolicy")
   .RequireRateLimiting("sliding")
   .MapSlices();
```


## Escape hatches

### Validation

**Default path:** DataAnnotations attributes on the request record — `[Required]`, `[MinLength]`,
`[EmailAddress]`, etc. Supported attributes are generated as compile-time validation and run before
endpoint filters.

Supported attributes: `Required`, `StringLength`, `MinLength`, `MaxLength`, `EmailAddress`, `Url`,
`RegularExpression`, `Range`.

**Escape hatch:** implement `ISliceValidator<TRequest>` for cross-field rules, async checks
(e.g., uniqueness against a store), custom attributes, or anything that needs DI.

```csharp
public sealed class CreateUserValidator : ISliceValidator<CreateUser.Request>
{
    private readonly IUserStore _store;

    public CreateUserValidator(IUserStore store) => _store = store;

    public async ValueTask<SliceValidationResult> ValidateAsync(CreateUser.Request value, CancellationToken ct)
    {
        if (await _store.EmailExistsAsync(value.Email, ct))
            return SliceValidationResult.Failure("Email", "Email is already registered.");
        return SliceValidationResult.Success;
    }
}
```

The generator discovers and registers the validator automatically. Generated DataAnnotations checks
run first; `ISliceValidator<T>` runs after and before `[Filter<T>]` filters.

See [Design decisions FAQ — Why DataAnnotations *and* ISliceValidator\<T\>?](../design-decisions.md#why-dataannotations-and-islicevalidatort)
for the full rationale.

### Authorization

**Default path:** `[Authorize]` on the endpoint or group-level `RequireAuthorization()`.

**Escape hatch:** fallback authorization policy via `builder.Services.AddAuthorizationBuilder()`,
or per-endpoint `RequireAuthorization("PolicyName")` through a route group.

```csharp
// Fallback: require authentication for all endpoints except those explicitly allowing anonymous
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
```

Do not use `[Filter<T>]` as a replacement for the authorization system. Slice filters are for
explicit per-feature endpoint behavior, not for security boundaries.

### Rate limiting, output caching, CORS

Route groups are the idiomatic scope for these policies:

```csharp
app.MapGroup("/api")
   .RequireRateLimiting("sliding")
   .CacheOutput("default")
   .RequireCors("AllowFrontend")
   .MapSlices();
```

### Cross-cutting behavior

**Default path:** `[Filter<T>]` endpoint filter for per-feature concerns (logging, API-key checks,
audit, etc.).

**Escape hatch:** standard ASP.NET Core middleware for concerns that apply to all requests or to
non-SliceFx endpoints too.

## DI binding: [FromServices] and concrete service types

On POST/PUT/PATCH handlers the generator infers the request body from a single request-like DTO.
Interface and abstract DI dependencies are inferred from DI automatically, but **concrete service
types must be annotated with `[FromServices]`** so the generator doesn't treat them as the body.

```csharp
public static class PromoteUser
{
    public record Request([Required, MinLength(1)] string Tier);

    // AuditLog is a concrete class — must use [FromServices]
    // IClock is an interface — inferred from DI automatically
    public static async Task<Response> Handle(
        Guid id,
        Request req,
        [FromServices] AuditLog audit,
        IClock clock,
        CancellationToken ct)
    {
        // ...
    }
}
```

Keyed services require `[FromKeyedServices(key)]`:

```csharp
public static async Task<Response> Handle(
    Request req,
    [FromKeyedServices("promotion")] IClock clock,
    CancellationToken ct)
```

See `samples/SliceFx.Sample/Features/Users/PromoteUser.cs` for a full working example.
