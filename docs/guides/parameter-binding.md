# Parameter binding across hosting targets

SliceFx maps the same `Handle` signature to every supported host. Because each host resolves
parameters differently, binding rules differ by target. The key distinction:

| Question | ASP.NET Core | Portable (WASI / Lambda) |
|---|---|---|
| Who decides binding? | ASP.NET Core's runtime binder | Source generator at compile time |
| Concrete DI service, no attribute | Resolved from DI — same as raw Minimal API | Treated as a body candidate → route excluded (SLICE023/033) unless annotated |
| `[FromServices]` required? | No — optional / harmless | Yes, for concrete service types |

## ASP.NET Core (Kestrel / TestHost)

The source generator emits a plain `MapMethods` call over a bare delegate — no binding
annotations are injected. Binding follows **ASP.NET Core's own inference exactly**, in this
order:

1. Explicit attributes (`[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`,
   `[FromServices]`, `[FromKeyedServices]`)
2. Special types (`HttpContext`, `HttpRequest`, `HttpResponse`, `CancellationToken`, …)
3. Simple types with a `TryParse` → route (if a matching `{token}` exists) or query string.
4. Any type registered in the DI container — concrete **or** interface — is resolved as a
   service. ASP.NET Core uses `IServiceProviderIsService` for this check.
5. A single remaining complex type → JSON request body.

This is **identical to raw Minimal API**. `[FromServices]` is therefore **never required** for
a registered service on the ASP.NET path — it is optional and has no effect on binding.

```csharp
// Works on ASP.NET without [FromServices] because AuditLog is AddSingleton<AuditLog>()
public static Task<Response> Handle(Request req, AuditLog audit, CancellationToken ct) { ... }
```

The generated DataAnnotations validation filter gates on `IServiceProviderIsService` at runtime
and skips any parameter that resolves as a registered service, so it only validates parameters
ASP.NET binds from the request body. `ISliceValidator<T>` filters are attached at compile time
only to request-like (body) parameters — neither validator fires against a DI-resolved service.

## Portable dispatch (WASI / Lambda function-per-feature)

The source generator must resolve all bindings at **compile time** because there is no runtime
service provider to probe. It applies a static convention:

1. Explicit attributes (`[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`,
   `[FromServices]`, `[FromKeyedServices(key)]`) — honored exactly.
2. `CancellationToken` → resolved as a service.
3. Interface or abstract complex type → resolved as a DI service.
4. **Concrete, non-framework complex type on POST / PUT / PATCH → request body.**
5. Simple types → route (if matching `{token}`) or query string.

A concrete DI service that is **not annotated** with `[FromServices]` is classified as a body
candidate by rule 4. If another parameter is also body-eligible (e.g. the request DTO), there
are now two body candidates. Multiple body parameters are not supported, so **the entire
feature is excluded from the portable route table**:

- WASI → **SLICE023** (`WasiRegistrationEmitter.cs`, Warning)
- Lambda function-per-feature → **SLICE033** (`LambdaFunctionPerFeatureEmitter.cs`, Warning)

`[FromServices]` reclassifies the parameter to Services (rule 1), leaving exactly one body and
keeping the route portable.

```csharp
// Portable across ASP.NET, WASI, and Lambda
public static async Task<Response> Handle(
    Guid id,
    Request req,
    [FromServices] AuditLog audit,           // concrete — must annotate for portability
    [FromKeyedServices("promotion")] IClock clock,  // keyed service
    CancellationToken ct) { ... }
```

See `samples/SliceFx.Sample/Features/Users/PromoteUser.cs` for a full working example.

## Recommendation

Annotate concrete service parameters with `[FromServices]` (keyed services with
`[FromKeyedServices(key)]`) **only if** you want the handler to be portable across ASP.NET,
WASI, and Lambda. For ASP.NET-only handlers it is optional. Interface and abstract-typed
dependencies are always inferred from DI on all paths and require no annotation.
