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

## DI binding

On the ASP.NET path the generated code is plain Minimal API — no binding annotations are
injected. ASP.NET Core resolves **any registered service** (concrete or interface) from DI via
its built-in `IServiceProviderIsService` check; `[FromServices]` is never required here and
behaves identically to raw Minimal API.

> **Portability note:** Annotate concrete service parameters with `[FromServices]` (keyed
> services with `[FromKeyedServices(key)]`) if you want the handler to be portable across
> ASP.NET, WASI, and Lambda. The portable-dispatch generator uses a compile-time heuristic
> and cannot probe the DI container; an un-annotated concrete service becomes a second body
> candidate and the feature is excluded from the portable route table (SLICE023/SLICE033).
>
> See [Parameter binding across hosting targets](parameter-binding.md) for the full rules.

`samples/SliceFx.Sample/Features/Users/PromoteUser.cs` demonstrates the pattern.
