# Hono/Vercel-inspired direction

Slice should learn from Hono and Vercel without copying their JavaScript APIs. The project remains a .NET framework built on ASP.NET Core Minimal APIs, source generation, static handlers, and vertical slice boundaries.

## What is already true

- A feature is one public static class with one public static `Handle` method.
- Route metadata is explicit through `[Feature("METHOD /path")]`.
- Cross-cutting behavior uses existing ASP.NET Core `IEndpointFilter`s through `[Filter<T>]`.
- The source generator emits registration code for AOT-friendly startup.
- The source generator emits route metadata for tooling and deployment experiments.
- `Slice.Workers` proves that the same feature shape can run outside ASP.NET when the handler signature and return type are portable.
- `Slice.Lambda`, `Slice.TestHost`, Workers, and the CLI are satellite packages so optional dependencies stay out of `Slice.Core`.

## Hono ideas to adopt

- **Small routing surface.** Keep feature registration mechanical and easy to inspect. Avoid a second fluent router unless it solves a clear .NET-specific problem.
- **Runtime portability.** Treat Workers/fetch-style dispatch as the lightweight runtime proving ground. A portable feature should not need ASP.NET-only response types.
- **Middleware at the edge of the feature.** Keep using endpoint filters instead of inventing a mediator pipeline. Workers can support only the filter concepts that make sense without ASP.NET.
- **Simple request/response primitives.** `WorkerRequest`, `WorkerResponse`, `SliceResult`, POCOs, `Task<T>`, and `ValueTask<T>` should remain the portable path.

## Vercel ideas to adopt

- **Convention-first project shape.** `Features/<Group>/<Feature>.cs` should stay easy to scaffold and navigate.
- **Generated route metadata.** The source generator is the natural place to emit a route manifest for tooling, docs, compatibility checks, and future deployment output.
- **Deploy target awareness.** Tooling should be able to explain whether a feature is ASP.NET-only, Workers-compatible, or potentially function-per-feature compatible.
- **One slice as a deployment boundary.** Function-per-feature deployment is a design goal, but not the current Lambda behavior. Today Lambda hosts the ASP.NET app as one entry point.

## Generated route metadata

The generated route manifest is the shared metadata seam for Hono/Vercel-inspired tooling. It is emitted into `Slice.Generated` by the source generator and contains:

- HTTP method and route pattern.
- Feature type, inferred tag, endpoint name, and summary.
- Request type and return type names.
- Referenced filter type names.
- A Workers compatibility flag based on whether the return type is ASP.NET-specific.

The manifest is deliberately string-based. Deployment tools, docs, and CLI commands can read route shape and compatibility without adding runtime dependencies to `Slice.Core`.

## Non-goals

- Do not clone Hono's `app.get(...)` API as a parallel authoring model by default.
- Do not replace ASP.NET Minimal APIs with a custom HTTP stack for the main runtime.
- Do not add mediator-style abstractions such as `IMediator` or `IPipelineBehavior`.
- Do not add package dependencies to `Slice.Core`.
- Do not introduce per-request reflection.
- Do not make file-system routing mandatory; explicit attributes keep routes visible in code and generator-friendly.

## Portable feature guidance

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

Use POCOs or `SliceResult`/`WorkerResponse` when the feature should be portable to Workers-style dispatch:

```csharp
[Feature("GET /health")]
public static class GetHealth
{
    public record Response(string Status);

    public static Response Handle()
        => new("ok");
}
```

The generator currently excludes ASP.NET-specific `IResult` features from Workers route tables with `SLICE008`.

## Workers/fetch-style direction

`Slice.Workers` is the stable experiment for a Hono-like fetch runtime. Its minimal dispatch surface is `WorkerRequest` in and `WorkerResponse` out, with `SliceResult` helpers for common responses. The route table should stay generated and deterministic; non-validator ASP.NET endpoint filters should not be assumed to run in Workers.

Near-term Workers work should focus on better manifest-driven compatibility reporting and source-generated JSON/validation coverage before introducing another public routing API.

## Deployment direction

The near-term Vercel-like target is full-app deployment with generated route metadata. Function-per-feature output remains a design goal, but it should be built on top of a stable manifest rather than a new authoring model.

This keeps today's ASP.NET and Lambda behavior intact while creating a path for future build tooling to split slices into finer deployment units.

## Next implementation direction

1. Use the generated route manifest to improve CLI output and documentation.
2. Add manifest-driven compatibility checks where they provide clear feedback.
3. Keep Workers/fetch-style dispatch focused on `WorkerRequest` -> `WorkerResponse`.
4. Revisit function-per-feature build output after the manifest shape is stable.
