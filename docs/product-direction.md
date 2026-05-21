# Product direction

Slice should stay focused on a simple product promise: one feature file per endpoint, portable across ASP.NET, AWS Lambda, and Cloudflare Workers without changing the feature code. Authors keep the standard ASP.NET Core model, write one explicit feature file per endpoint, and let Slice generate the surrounding API contracts and tooling from that shape. Hono and Vercel are useful references, but the public story should be Slice's own: portable .NET API features with generated contracts.

## Product principles

- **Explicit over magic.** Routes are declared with `[Feature("METHOD /path")]`; conventions help tooling but should not hide the HTTP contract.
- **Standard by default.** Reuse Minimal API binding, DI, endpoint filters, DataAnnotations, and `IResult` instead of replacing ASP.NET Core with a parallel framework.
- **Small surface area.** Prefer one obvious authoring model over multiple clever APIs.
- **Generated, not hand-synced.** Route metadata, compatibility checks, and typed clients should come from the feature shape.

## Primary users

- **AOT, serverless, and Workers-minded teams** that want the same feature file to run on ASP.NET, Lambda, and Cloudflare Workers, with portability classified at build time rather than discovered at deploy time.
- **Small API and product teams** that want endpoint code to stay local to the feature instead of spreading across controllers, services, validators, and mapping profiles.
- **Blazor and .NET client teams** that want typed clients generated from server routes instead of hand-maintained route strings and DTO wiring.
- **Framework-light .NET teams** that prefer ASP.NET Core Minimal API binding and endpoint filters over mediator-style abstractions or required third-party validation stacks.

## What is already true

- A feature is one public static class with one public static `Handle` method.
- Route metadata is explicit through `[Feature("METHOD /path")]`.
- Cross-cutting behavior uses existing ASP.NET Core `IEndpointFilter`s through `[Filter<T>]`.
- The source generator emits registration code for AOT-friendly startup.
- The source generator emits route metadata for tooling and deployment experiments.
- `Slice.Workers` proves that the same feature shape can run outside ASP.NET when the handler signature and return type are portable.
- `slice routes` reports whether discovered features are `portable`, `partial`, or `aspnet-only` for Workers-style dispatch, and can export route metadata as JSON.
- `slice client csharp` generates a typed `HttpClient` wrapper for portable and partial routes, giving Blazor and other .NET clients a Hono RPC-like starting point.
- `Slice.Lambda`, `Slice.TestHost`, Workers, and the CLI are satellite packages so optional dependencies stay out of `Slice.Core`.

## Hono-inspired references

These ideas are useful references, not the public authoring model. Slice should not lead with Hono terminology; it should lead with standard .NET APIs plus generated contracts. Leading with Cloudflare Workers or Lambda portability is distinct from leading with Hono vocabulary — the former is Slice's own positioning, the latter borrows a competitor's brand.

- **Small routing surface.** Keep feature registration mechanical and easy to inspect. Avoid a second fluent router unless it solves a clear .NET-specific problem.
- **Runtime portability.** Treat Workers/fetch-style dispatch as the lightweight runtime proving ground. A portable feature should not need ASP.NET-only response types.
- **Middleware at the edge of the feature.** Keep using endpoint filters instead of inventing a mediator pipeline. Workers can support only the filter concepts that make sense without ASP.NET.
- **Simple request/response primitives.** `WorkerRequest`, `WorkerResponse`, `SliceResult`, POCOs, `Task<T>`, and `ValueTask<T>` should remain the portable path.
- **Typed clients from routes.** Hono's RPC story turns server routes into client types. Slice should answer that in .NET by generating C# typed clients for Blazor/.NET consumers, and later TypeScript clients for browser/edge consumers.

## Vercel-inspired references

These ideas are useful deployment references, but function-per-feature output remains future-facing. The current value is generated metadata and compatibility visibility without changing the app model.

- **Convention-first project shape.** `Features/<Group>/<Feature>.cs` should stay easy to scaffold and navigate.
- **Generated route metadata.** The source generator is the natural place to emit a route manifest for tooling, docs, compatibility checks, and future deployment output.
- **Deploy target awareness.** Tooling should be able to explain whether a feature is ASP.NET-only, Workers-compatible, or potentially function-per-feature compatible.
- **One slice as a deployment boundary.** Function-per-feature deployment is a design goal, but not the current Lambda behavior. Today Lambda hosts the ASP.NET app as one entry point.

## Generated route metadata

The generated route manifest is the shared metadata seam for Hono/Vercel-inspired tooling. It is emitted into the `Slice` namespace by the source generator and contains:

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
```

`slice routes` makes portability visible at the slice boundary:

- `portable` means the route avoids ASP.NET-specific return types and can be considered for Workers-style dispatch.
- `partial` means the route shape is portable, but some attached behavior such as non-validator endpoint filters is ASP.NET-only today.
- `aspnet-only` means the route intentionally depends on ASP.NET concepts such as `IResult`.

`slice client csharp` generates a typed `HttpClient` wrapper for portable and partial routes. This is the .NET/Blazor-oriented counterpart to Hono's typed client story: write the server feature once, then let tooling produce the client entrypoint instead of manually maintaining endpoint strings and DTO wiring.

## Non-goals

- Do not clone Hono's `app.get(...)` API as a parallel authoring model by default.
- Do not replace ASP.NET Minimal APIs with a custom HTTP stack for the main runtime.
- Do not add mediator-style abstractions such as `IMediator` or `IPipelineBehavior`.
- Do not add package dependencies to `Slice.Core`.
- Do not introduce per-request reflection.
- Do not make file-system routing mandatory; explicit attributes keep routes visible in code and generator-friendly.

## Portable feature guidance

Prefer POCO response records when a feature should stay visible to route metadata, typed clients, and Workers-style portability:

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

For Workers-specific responses, use `SliceResult` or `WorkerResponse`. The generator currently excludes ASP.NET-specific `IResult` features from Workers route tables with `SLICE008`.

## Workers/fetch-style direction

`Slice.Workers` is the active experiment for a Hono-like fetch runtime. Its minimal dispatch surface is `WorkerRequest` in and `WorkerResponse` out, with `SliceResult` helpers for common responses. The route table should stay generated and deterministic; non-validator ASP.NET endpoint filters should not be assumed to run in Workers.

Near-term Workers work should focus on better manifest-driven compatibility reporting and source-generated JSON/validation coverage before introducing another public routing API.

## Deployment direction

`slice manifest aws-lambda` (CLI) generates an AWS SAM `template.yaml` with one `AWS::Serverless::Function` per `[Feature]`, using the source-generated route manifest. This is the Phase 1 "function-per-feature" story for Lambda: same binary, separate Lambda functions with independent routing, timeout, and memory configuration. ASP.NET route constraints are automatically stripped for API Gateway. The default runtime is `provided.al2023` (NativeAOT / self-contained, handler `bootstrap`).

Phase 2 (separate Lambda handler classes per feature, emitted by the source generator into a new `Slice.Lambda.PerFunction` satellite) remains a design goal, building on the stable manifest. Phase 3 (separate NativeAOT binaries per feature for smaller cold-start footprint) is long-term.

Cloudflare Workers function-per-feature deployment (one WASM component per feature) requires per-feature NativeAOT compilation and is blocked on `componentize-dotnet` multi-component build support. The current Slice.Workers model (one WASM component, all routes dispatched in-process) is the practical deployment target until that tooling matures.

Even when the build tooling matures, the benefit of per-feature Workers deployment is less clear than for Lambda: Cloudflare Workers are edge functions with negligible cold-start latency, no per-function memory or timeout configuration, and no per-route scaling or billing isolation. The main remaining benefit would be independent deployment per feature (deploying one route without affecting others). That benefit alone may not justify the build complexity unless `componentize-dotnet` makes per-feature compilation straightforward.

## Next implementation direction

1. ~~Continue converging CLI route discovery on the source-generated route manifest.~~ ✅ Done.
2. Extend typed client generation from C# first to TypeScript once the route metadata is rich enough.
3. Add manifest-driven deployment checks where they provide clear feedback.
4. Keep Workers/fetch-style dispatch focused on `WorkerRequest` -> `WorkerResponse`.
5. ~~Revisit function-per-feature build output after the manifest shape is stable.~~ ✅ Done (Lambda Phase 1: `slice manifest aws-lambda`). Lambda Phase 2 (source-generated per-feature handlers) is the next step.
