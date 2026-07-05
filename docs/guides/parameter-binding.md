# Parameter binding across hosting targets

[日本語](../ja/guides/parameter-binding.md)

SliceFx maps the same `Handle` signature to every supported host. Because each host resolves
parameters differently, binding rules differ by target. The key distinction:

| Question | ASP.NET Core (JIT / Kestrel) | ASP.NET NativeAOT (`SliceAspNetAot`) | Portable (WASI / Lambda) |
|---|---|---|---|
| Who decides binding? | ASP.NET Core's **runtime** binder | Source generator at **compile time** | Source generator at **compile time** |
| Concrete DI service, no attribute | Resolved from DI via `IServiceProviderIsService` (JSON-context irrelevant) | Resolved from DI, **unless** it is the sole request-like candidate on a body verb and is registered in the JSON context — then it's the body (see [Body selection](#body-selection-compile-time-paths)) | Same precedence as ASP.NET NativeAOT — DI unless it's the sole body-verb candidate in the JSON context |
| `[FromServices]` required? | No — optional / harmless | Only for the residual case above | Only for the residual case above |

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
The binder resolves each parameter's source as follows (`ResolveConventionBinding` in
`SourceGenerationHelpers.cs`, shared by `AspNetAotRegistrationEmitter`):

1. Explicit attributes (`[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`,
   `[FromServices]`, `[FromKeyedServices(key)]`) — honored exactly.
2. `CancellationToken` → resolved as a service.
3. Interface or abstract complex type → resolved as a DI service (no annotation needed).
4. **At most one** remaining concrete, non-framework complex parameter is chosen as the request
   body, using the precedence in [Body selection](#body-selection-compile-time-paths) below.
   Every concrete parameter *not* selected as the body is resolved from DI.
5. Simple types → route (if matching `{token}`) or query string.

A handler with a nested `Request` record and any number of injected concrete services now
compiles without annotation — the nested type wins the body slot (precedence 2 below) and
everything else falls through to DI. **SLICE070 (Error)** only fires when the selection is
genuinely ambiguous: two candidates tie at the same precedence level (e.g. two `[FromBody]`
parameters, two nested types, or two sole-JSON-context candidates). The diagnostic names the
second candidate and says it "is a second request-body candidate; a handler binds at most one
request body" — annotate the intended body with `[FromBody]`, mark the other with
`[FromServices]`, or use an interface/abstract type so only one candidate remains. Unlike
SLICE023 (WASI Warning) and SLICE033 (Lambda Warning), SLICE070 is an **Error** — the build fails.

```csharp
// [assembly: SliceAspNetAot] is set — compile-time binding.
// NpgsqlDataSource is NOT in [SliceJsonContext(AspNet)] and isn't a nested type of the
// feature — it's always a DI service. No [FromServices] needed.
public static async Task<SliceResult> Handle(NpgsqlDataSource db, CancellationToken ct) { ... }

// Idiomatic shape: Request (nested type) wins the body slot by precedence 2; AuditLog
// resolves from DI even though it's registered in [SliceJsonContext(AspNet)] — it's no
// longer the sole request-like candidate once the nested Request is picked. Compiles as-is.
public static async Task<SliceResult<Response>> Handle(
    Request req,
    AuditLog audit,
    CancellationToken ct) { ... }

// Two candidates tie (no nested Request to break the tie, both in the JSON context) → SLICE070.
// Fix: use an interface or add [FromServices] to the one that isn't the body.
public static async Task<SliceResult<Response>> Handle(
    CreateOrderPayload payload,
    [FromServices] AuditLog audit,   // without [FromServices] → SLICE070 on POST
    CancellationToken ct) { ... }
```

## Portable dispatch (WASI / Lambda function-per-feature)

The source generator must resolve all bindings at **compile time** because there is no runtime
service provider to probe. It applies the same `SelectBodyParameter` precedence used by ASP.NET
NativeAOT (see [Body selection](#body-selection-compile-time-paths) below):

1. Explicit attributes (`[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`,
   `[FromServices]`, `[FromKeyedServices(key)]`) — honored exactly.
2. `CancellationToken` → resolved as a service.
3. Interface or abstract complex type → resolved as a DI service.
4. **At most one** remaining concrete, non-framework complex parameter is chosen as the request
   body (nested `Request` type first, then the sole JSON-context-registered candidate, POST/PUT/
   PATCH only). Every other concrete parameter is resolved from DI.
5. Simple types → route (if matching `{token}`) or query string.

A handler with a nested `Request` record plus injected concrete services is portable without
any `[FromServices]` annotations — the nested type wins the body slot and the rest fall through
to DI. Ambiguity only arises when two parameters tie at the same precedence (e.g. two
JSON-context-registered candidates with no nested type to break the tie). When that happens,
**the entire feature is excluded from the portable route table**:

- WASI → **SLICE023** (`WasiRegistrationEmitter.cs`, Warning)
- Lambda function-per-feature → **SLICE033** (`LambdaFunctionPerFeatureEmitter.cs`, Warning)

Unlike SLICE070 on the ASP.NET NativeAOT path, SLICE023/SLICE033 messages report the excluded
parameter/type but are not the detailed "second request-body candidate" wording — they are
Warnings, not build-breaking Errors. `[FromServices]` reclassifies a tied parameter to Services
(rule 1), leaving exactly one body candidate and keeping the route portable.

```csharp
// Portable across ASP.NET, WASI, and Lambda
public static async Task<Response> Handle(
    Guid id,
    Request req,                                    // nested type wins the body slot
    [FromServices] AuditLog audit,                   // concrete — resolves from DI either way;
                                                      // [FromServices] documents intent and is
                                                      // required only if AuditLog were the sole
                                                      // remaining request-like candidate
    [FromKeyedServices("promotion")] IClock clock,    // keyed service
    CancellationToken ct) { ... }
```

See `samples/SliceFx.Sample/Features/Users/PromoteUser.cs` for a full working example (its doc
comments predate this precedence change and describe `[FromServices]` as required; it remains
correct and portable either way, but is no longer the minimal example of the convention).

## Body selection (compile-time paths)

ASP.NET NativeAOT, WASI, and Lambda function-per-feature share one selector
(`SelectBodyParameter` in `SourceGenerationHelpers.cs`) that resolves **at most one** request
body per handler. It applies, in order:

1. **`[FromBody]`** — the explicitly annotated parameter, on any HTTP method.
2. **Convention** — on `POST`/`PUT`/`PATCH`, the parameter whose type is nested in the feature
   class (the canonical `Request` record).
3. **Sole serializable candidate** — on `POST`/`PUT`/`PATCH`, the single remaining request-like
   parameter registered in a `[SliceJsonContext]` (covers non-nested shared contracts used by
   generated clients).
4. **Otherwise → DI**, including every `GET`/`DELETE` handler (which never infer a body).

Every parameter not selected as the body is resolved from DI. Interfaces, abstract types, and
`[FromServices]`-annotated parameters are always DI and never enter the body candidate set.

You no longer need to make every injected service an interface, or wrap plain values in
`IConfiguration`, to avoid a false second-body error: an injected concrete type alongside a
nested `Request` resolves from DI automatically (precedence 2 already claimed the body slot).
Only a **genuine tie** — two candidates at the same precedence level, with nothing earlier in
the list to break it — reports a diagnostic asking you to disambiguate with
`[FromBody]`/`[FromServices]`: SLICE070 (ASP.NET NativeAOT, Error) carries this exact wording;
SLICE023 (WASI) and SLICE033 (Lambda function-per-feature) are Warnings that exclude the route
instead of failing the build, and their messages are not as detailed.

**Residual cases:**

- A concrete type that is **both** a DI service **and** registered in a `[SliceJsonContext]`,
  and is the **only** request-like parameter on a body verb, is treated as the body (precedence
  3) — annotate it `[FromServices]` or change it to an interface if you want it resolved from DI
  instead.
- A type selected as the body but never registered in DI compiles successfully; it fails at DI
  resolution at runtime, the same as raw ASP.NET Core Minimal API. There is no compile-time
  check that a selected body type is actually registered.

## Recommendation

Rely on the nested `Request` convention for the body and let concrete services resolve from DI
implicitly — this is now portable across ASP.NET, WASI, and Lambda without annotation.
Annotate a concrete service with `[FromServices]` (keyed services with
`[FromKeyedServices(key)]`) only when it would otherwise tie for the body slot (see residual
cases above) or when you want to document intent explicitly. Interface and abstract-typed
dependencies are always inferred from DI on all paths and require no annotation.
