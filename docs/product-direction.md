# Product direction

Slice should stay focused on a simple product promise: one feature file per endpoint, portable across ASP.NET-hosted apps, serverless functions, and WASI hosts without changing the feature code. Authors keep the standard ASP.NET Core model, write one explicit feature file per endpoint, and let Slice generate the surrounding API contracts and tooling from that shape. The public story should be Slice's own: portable .NET API features with generated contracts.

## Product principles

- **Explicit over magic.** Routes are declared with `[Feature("METHOD /path")]`; conventions help tooling but should not hide the HTTP contract.
- **Standard by default.** Reuse Minimal API binding, DI, endpoint filters, DataAnnotations, and `IResult` instead of replacing ASP.NET Core with a parallel framework.
- **Small surface area.** Prefer one obvious authoring model over multiple clever APIs.
- **Generated, not hand-synced.** Route metadata, compatibility checks, and typed clients should come from the feature shape.

## Primary users

- **AOT, serverless, and WASI-minded teams** that want the same feature file to run across ASP.NET-hosted apps, function hosts, and wasi:http hosts, with portability classified at build time rather than discovered at deploy time.
- **Small API and product teams** that want endpoint code to stay local to the feature instead of spreading across controllers, services, validators, and mapping profiles.
- **Blazor and .NET client teams** that want typed clients generated from server routes instead of hand-maintained route strings and DTO wiring.
- **Framework-light .NET teams** that prefer ASP.NET Core Minimal API binding and endpoint filters over mediator-style abstractions or required third-party validation stacks.

## What is already true

- A feature is one public static class with one public static `Handle` method.
- Route metadata is explicit through `[Feature("METHOD /path")]`.
- Cross-cutting behavior uses existing ASP.NET Core `IEndpointFilter`s through `[Filter<T>]`.
- The source generator emits registration code for AOT-friendly startup.
- The source generator emits route metadata for tooling and deployment experiments.
- `Slice.Wasi` proves that the same feature shape can run outside ASP.NET when the handler signature and return type are portable.
- `slice routes` reports whether discovered features are `portable`, `partial`, or `aspnet-only` for WASI-style dispatch, and can export route metadata as JSON.
- `slice client csharp` generates a typed `HttpClient` wrapper for portable and partial routes, giving Blazor and other .NET clients a generated-contract starting point.
- `Slice.Lambda`, `Slice.TestHost`, WASI, and the CLI are satellite packages so optional dependencies stay out of `Slice.Core`.

## Typed-client and runtime references

These ideas are useful references, not the public authoring model. Slice should lead with standard .NET APIs plus generated contracts instead of naming other frameworks or specific deployment vendors in the product promise.

- **Small routing surface.** Keep feature registration mechanical and easy to inspect. Avoid a second fluent router unless it solves a clear .NET-specific problem.
- **Runtime portability.** Treat WASI/fetch-style dispatch as the lightweight runtime proving ground. A portable feature should not need ASP.NET-only response types.
- **Middleware at the edge of the feature.** Keep using endpoint filters instead of inventing a mediator pipeline. WASI can support only the filter concepts that make sense without ASP.NET.
- **Simple request/response primitives.** `WasiRequest`, `WasiResponse`, `SliceResult`, POCOs, `Task<T>`, and `ValueTask<T>` should remain the portable path.
- **Typed clients from routes.** Server route metadata should turn into client entrypoints. Slice should answer that in .NET by generating C# typed clients for Blazor/.NET consumers, and later TypeScript clients for browser/edge consumers.

## Per-feature deployment references

These ideas are useful deployment references, not a platform compatibility claim. The feature boundary should be visible to tooling and can become a deployment boundary where the runtime supports it. That does not mean file-system routing is mandatory, or that Slice emits one independent binary or WASM component per feature.

The current value is generated metadata, compatibility visibility, hosted deployment manifests, and an HTTP API v2 per-feature Lambda MVP without changing the app model. Fully independent NativeAOT binary-per-feature output remains future-facing.

- **Convention-first project shape.** `Features/<Group>/<Feature>.cs` should stay easy to scaffold and navigate.
- **Generated route metadata.** The source generator is the natural place to emit a route manifest for tooling, docs, compatibility checks, and future deployment output.
- **Deploy target awareness.** Tooling should be able to explain whether a feature is ASP.NET-only, WASI-compatible, or potentially function-per-feature compatible.
- **One slice as a deployment boundary.** Function-per-feature Lambda deployment is available as an explicit HTTP API v2 MVP. The default Lambda path remains `Slice.Lambda`, which hosts the ASP.NET app as one entry point.

## Generated route metadata

The generated route manifest is the shared metadata seam for client generation, deployment tooling, and compatibility checks. It is emitted into the `Slice` namespace by the source generator and contains:

- HTTP method and route pattern.
- Feature type, inferred tag, endpoint name, and summary.
- Request type, return type, and handler parameter names.
- Referenced filter type names.
- Portability status using `portable`, `partial`, or `aspnet-only`.

The manifest is deliberately string-based. Deployment tools, docs, and CLI commands can read route shape and compatibility without adding runtime dependencies to `Slice.Core`. The CLI route catalog should prefer generated metadata from built project outputs, then fall back to source scanning only for unbuilt projects. Route listing, compatibility reporting, typed client generation, OpenAPI, and future deployment checks should share this contract.

## CLI-generated tooling

The CLI is the first user-visible surface for this direction:

```bash
slice routes
slice routes --format json
slice client csharp --output SliceApiClient.g.cs
slice client typescript --output slice-api-client.ts
slice openapi --output openapi.json
```

`slice routes` makes portability visible at the slice boundary:

- `portable` means the route avoids ASP.NET-specific return types and can be considered for WASI-style dispatch.
- `partial` means the route shape is portable, but some attached behavior such as non-validator endpoint filters is ASP.NET-only today.
- `aspnet-only` means the route intentionally depends on ASP.NET concepts such as `IResult`.

`slice client csharp` and `slice client typescript` generate typed clients for portable and partial routes: write the server feature once, then let tooling produce the client entrypoint instead of manually maintaining endpoint strings and DTO wiring.

`slice openapi` projects the same manifest into OpenAPI JSON for portable tooling without starting an ASP.NET host. The ASP.NET Core `AddOpenApi` / `MapOpenApi` document remains the authoritative hosted-app document; the CLI output is stamped as a manifest projection and must not invent metadata beyond the manifest.

`slice new wasi-cloudflare` scaffolds Cloudflare Workers deployment glue for the WASI path. The primary WASI sample also deploys natively to Fermyon Cloud / Spin because it emits a standard `wasi:http/incoming-handler` component. Runtime docs should present this as wasi:http portability first, with Cloudflare glue and Spin deployment as target-specific details. Keep the support language precise: `Slice.Wasi` is experimental 0.x API surface, while the upstream WASI build/transpile stack is preview/unstable and can break independently of Slice.

## Non-goals

- Do not clone another framework's fluent routing API as a parallel authoring model by default.
- Do not replace ASP.NET Minimal APIs with a custom HTTP stack for the main runtime.
- Do not add mediator-style abstractions such as `IMediator` or `IPipelineBehavior`.
- Do not add package dependencies to `Slice.Core`.
- Do not introduce per-request reflection.
- Do not make file-system routing mandatory; explicit attributes keep routes visible in code and generator-friendly.

## Portable feature guidance

Prefer POCO response records when a feature should stay visible to route metadata, typed clients, and WASI-style portability:

```csharp
[Feature("POST /users")]
public static class CreateUser
{
    public record Request(string Name, string Email);
    public record Response(Guid Id, string Name, string Email);

    public static async Task<Response> Handle(Request req, IUserStore store, CancellationToken ct)
    {
        var user = await store.AddAsync(req.Name, req.Email, ct).ConfigureAwait(false);
        return new Response(user.Id, user.Name, user.Email);
    }
}
```

Use `IResult` when a feature is intentionally ASP.NET-specific and should use Minimal API response helpers:

```csharp
[Feature("GET /users/{id:guid}")]
public static class GetUser
{
    public static async Task<IResult> Handle(Guid id, IUserStore store, CancellationToken ct)
    {
        var user = await store.GetAsync(id, ct).ConfigureAwait(false);
        return user is null ? Results.NotFound() : Results.Ok(user);
    }
}
```

For WASI-specific responses, use `SliceResult` or `WasiResponse`. The generator currently excludes ASP.NET-specific `IResult` features from WASI route tables with `SLICE008`.

## WASI/fetch-style direction

`Slice.Wasi` is the active experiment for a lightweight wasi:http runtime. Its minimal dispatch surface is `WasiRequest` in and `WasiResponse` out, with `SliceResult` helpers for common responses. The route table should stay generated and deterministic; non-validator ASP.NET endpoint filters should not be assumed to run in the WASI path.

Near-term WASI work should focus on better manifest-driven compatibility reporting and source-generated JSON/validation coverage before introducing another public routing API.

## Deployment direction

`slice manifest aws-lambda` (CLI) generates an AWS SAM `template.yaml` from the source-generated route manifest. The default hosted mode emits one `AWS::Serverless::Function` for the ASP.NET-hosted Slice app and one API Gateway `HttpApi` event per `[Feature]`. ASP.NET route constraints are automatically stripped for API Gateway. The default runtime is `provided.al2023` (NativeAOT / self-contained, handler `bootstrap`).

`--mode per-feature` is implemented as an HTTP API v2 MVP on the `Slice.Lambda.PerFunction` satellite. The source generator emits feature-specific handler methods when the assembly opts in with `[assembly: LambdaPerFunction]`; the SAM template emits one `AWS::Serverless::Function` per eligible feature. The first packaging path may share one publish artifact across functions and select the generated method via `Handler`; separate NativeAOT binaries per feature remain long-term.

### Lambda per-feature MVP scope

The first per-feature Lambda mode is intentionally narrower than ASP.NET-hosted Lambda. It targets API Gateway HTTP API v2 and supports route parameters, query parameters, JSON request bodies, DI services, and `CancellationToken`. Supported return shapes start with POCOs, `Task<T>`, and `ValueTask<T>`; additional result helpers can be added only if they do not recreate ASP.NET's endpoint pipeline.

The MVP excludes features that return `IResult`, depend on non-validator endpoint filters, require reflection-based validation, use unsupported route parameter types, or lack an explicit `[SliceJsonContext(SliceJsonTarget.LambdaPerFeature)]` context for AOT-safe JSON body/response metadata. ASP.NET-specific middleware behavior such as auth, CORS, or Problem Details customization remains outside the compile-time contract. API Gateway REST v1, ALB, non-HTTP triggers, per-feature WASM components, and separate NativeAOT binaries stay out of the MVP. Reuse the existing WASI-style binding concepts where practical so portable feature rules converge instead of splitting by host.

Cloudflare WASI function-per-feature deployment (one WASM component per feature) requires per-feature NativeAOT compilation and is blocked on `componentize-dotnet` multi-component build support. The current Slice.WASI model (one WASM component, all routes dispatched in-process) is the practical deployment target until that tooling matures.

Even when the build tooling matures, the benefit of per-feature WASI deployment is less clear than for function hosts with per-function scaling and billing knobs. The main remaining benefit would be independent deployment per feature (deploying one route without affecting others). That benefit alone may not justify the build complexity unless `componentize-dotnet` makes per-feature compilation straightforward.

## Next implementation direction

1. ~~Continue converging CLI route discovery on the source-generated route manifest.~~ ✅ Done.
2. Extend typed client generation from C# first to TypeScript once the route metadata is rich enough.
3. Add manifest-driven deployment checks where they provide clear feedback.
4. Keep WASI/fetch-style dispatch focused on `WasiRequest` -> `WasiResponse`.
5. Continue from generated per-feature Lambda handlers toward true NativeAOT binary-per-feature packaging. Treat separate NativeAOT binaries per feature as long-term, not current scope.
