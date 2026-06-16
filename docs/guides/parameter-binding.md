# Parameter binding across hosting targets

[日本語](../ja/guides/parameter-binding.md)

SliceFx maps the same `Handle` signature to every supported host. Because each host resolves
parameters differently, binding rules differ by target. The key distinction:

| Question | ASP.NET Core (JIT / Kestrel) | ASP.NET NativeAOT (`SliceAspNetAot`) | Portable (WASI / Lambda) |
|---|---|---|---|
| Who decides binding? | ASP.NET Core's **runtime** binder | Source generator at **compile time** | Source generator at **compile time** |
| Concrete DI service, no attribute | Resolved from DI via `IServiceProviderIsService` (JSON-context irrelevant) | JSON-context member + body verb → body candidate → **SLICE070 Error** unless annotated | Treated as a body candidate → route excluded (**SLICE023/033**) unless annotated |
| `[FromServices]` required? | No — optional / harmless | Yes, if concrete service is in JSON context and on a body verb | Yes, for concrete service types |

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

## ASP.NET NativeAOT (`[assembly: SliceAspNetAot]`)

When `[assembly: SliceAspNetAot]` is set, the source generator switches the ASP.NET registration
path to **compile-time binding** — runtime `IServiceProviderIsService` inference is not used.
The binding convention is the same as Portable dispatch (`ResolveConventionBinding` in
`SourceGenerationHelpers.cs`, shared by `AspNetAotRegistrationEmitter`):

1. Explicit attributes (`[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`,
   `[FromServices]`, `[FromKeyedServices(key)]`) — honored exactly.
2. `CancellationToken` → resolved as a service.
3. Interface or abstract complex type → resolved as a DI service (no annotation needed).
4. **Concrete, non-framework complex type registered in `[SliceJsonContext(AspNet)]` on POST / PUT / PATCH → request body candidate.**
   - Concrete type **not** in the JSON context on any verb → always resolved as a DI service, no diagnostic.
   - Concrete type in the JSON context on GET / HEAD / DELETE (no body verb) → always a DI service, no diagnostic.
5. Simple types → route (if matching `{token}`) or query string.

When two body candidates arise, the feature emits **SLICE070 (Error)**:
`"multiple body parameters are not supported"`. Unlike SLICE023 (WASI Warning) and SLICE033
(Lambda Warning), SLICE070 is an **Error** — the build fails. Fix by annotating the concrete
service with `[FromServices]`, or by using an interface type (rule 3 always routes interfaces to DI).

```csharp
// [assembly: SliceAspNetAot] is set — compile-time binding.
// NpgsqlDataSource is NOT in [SliceJsonContext(AspNet)], so it's always a DI service.
// No [FromServices] needed.
public static async Task<SliceResult> Handle(NpgsqlDataSource db, CancellationToken ct) { ... }

// AuditLog IS in [SliceJsonContext(AspNet)] AND this is POST → body candidate → SLICE070.
// Fix: use interface or add [FromServices].
public static async Task<SliceResult<Response>> Handle(
    Request req,
    [FromServices] AuditLog audit,   // without [FromServices] → SLICE070 on POST
    CancellationToken ct) { ... }
```

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
