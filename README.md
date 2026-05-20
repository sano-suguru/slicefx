# Slice

> A Vertical-Slice-first .NET web framework. **1 file = 1 feature = 1 deploy unit.**

Slice is an experimental .NET web framework built on top of ASP.NET Core. It sits at the intersection of three ideas:

- **FastEndpoints' shape** — 1 class = 1 endpoint, built on ASP.NET Core Minimal API.
- **FastAPI's DX** — declarative attributes, type-driven validation, parameter-based DI, OpenAPI for free.
- **axum's discipline** — static handlers, zero runtime reflection, AOT and serverless as first-class targets.

`Slice.Core` has zero NuGet package dependencies; the optional source generator lives in a separate project and uses Roslyn packages.

## Design principles

- **Vertical Slice first.** Each HTTP endpoint is one self-contained file. Request, response, handler, validator, filters — all together.
- **Static handlers.** Every `Handle` method is `static`. Dependencies come in as parameters. No instance state, no DI on the type itself, no reflection on the hot path after startup.
- **Zero new abstractions.** Slice doesn't invent `IPipelineBehavior<TReq, TRes>` or `IMediator` — it leans on ASP.NET Core's existing `IEndpointFilter`. Less to learn, less to maintain.
- **Declarative everything.** `[Feature]`, `[Filter<T>]`. The source generator or runtime fallback reads these and registers Minimal API endpoints, validation, and filter chains automatically.
- **AOT-ready structure.** Handlers are static and receive a strongly-typed delegate. The generated path avoids startup reflection; the runtime fallback keeps the same API shape for non-AOT scenarios.
- **Zero runtime framework dependencies.** Slice.Core has only one reference: `Microsoft.AspNetCore.App`. No FluentValidation, no MediatR, no AutoMapper. Roslyn dependencies are isolated to `Slice.SourceGenerator`.

## Development discipline

Changes are guarded by CI, a PR checklist, and a small set of project invariants. See [docs/development-discipline.md](docs/development-discipline.md) for the Definition of Done and local verification commands.

## Hello, Slice

```csharp
// Program.cs — AOT-friendly generated bootstrap
using Slice.Generated;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddSliceGenerated();
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();
var app = builder.Build();
app.MapSlicesGenerated();   // generated registrations for every [Feature]
app.Run();
```

```csharp
// Features/Users/CreateUser.cs — one feature, one file
[Feature("POST /users", Summary = "Create a new user")]
public static class CreateUser
{
    public record Request(
        [Required, MinLength(2)] string Name,
        [Required, EmailAddress] string Email);

    public record Response(Guid Id, string Name, string Email, DateTime CreatedAt);

    public static async Task<IResult> Handle(Request req, IUserStore store, CancellationToken ct)
    {
        var user = await store.AddAsync(req.Name, req.Email, ct);
        return Results.Created($"/users/{user.Id}",
            new Response(user.Id, user.Name, user.Email, user.CreatedAt));
    }
}
```

That's it. The framework:
1. Discovers `CreateUser` via the `[Feature]` attribute at compile time when the source generator is enabled.
2. Registers `POST /users` against the static `Handle` method.
3. Lets Minimal API auto-bind the body (`Request`), services (`IUserStore`), and the cancellation token.
4. Validates request DTOs via DataAnnotations attributes — including record positional parameters, type-level attributes, and `IValidatableObject`.
5. Returns Problem Details if validation fails.

## Cross-cutting concerns via `[Filter<T>]`

```csharp
[Feature("DELETE /users/{id:guid}", Summary = "Delete a user (requires API key)")]
[Filter<RequestLoggingFilter>]
[Filter<RequireApiKeyFilter>]
public static class DeleteUser
{
    public static async Task<IResult> Handle(Guid id, IUserStore store, CancellationToken ct)
    {
        var user = await store.GetAsync(id, ct);
        if (user is null) return Results.NotFound();
        await store.RemoveAsync(id, ct);
        return Results.NoContent();
    }
}
```

Filters are just `IEndpointFilter` implementations — no special interface. `AddSliceGenerated()` or `AddSlice()` auto-registers them in DI, and `[Filter<T>]` applies them in declaration order (outermost wraps innermost).

```csharp
public sealed class RequireApiKeyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (!ctx.HttpContext.Request.Headers.TryGetValue("X-API-Key", out var k) || k != "secret")
            return Results.Unauthorized();
        return await next(ctx);
    }
}
```

## Custom validation with `ISliceValidator<T>`

Use `ISliceValidator<T>` when DataAnnotations alone is not expressive enough, such as cross-field rules or async lookups. The validator stays in `Slice.Core`; there is no FluentValidation package dependency.

```csharp
[Feature("POST /echo", Summary = "Echo the request body back")]
[Filter<SliceValidatorFilter<Request>>]
public static class PostEcho
{
    public record Request([Required, MinLength(1)] string Message);

    public static IResult Handle(Request req)
        => Results.Ok(new { echo = req.Message });
}

public sealed class EchoRequestValidator : ISliceValidator<PostEcho.Request>
{
    public ValueTask<SliceValidationResult> ValidateAsync(PostEcho.Request value, CancellationToken ct)
    {
        if (value.Message.Contains("BLOCKED", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(SliceValidationResult.Failure(
                nameof(value.Message), "Message contains disallowed content."));
        }

        return ValueTask.FromResult(SliceValidationResult.Success);
    }
}
```

Register validators manually in `Program.cs`:

```csharp
using Slice;

builder.Services.AddScoped<ISliceValidator<PostEcho.Request>, EchoRequestValidator>();
```

`DataAnnotationsValidationFilter` is always attached first. `SliceValidatorFilter<TRequest>` is a normal `[Filter<T>]`, so it runs in declaration order with any other feature filters.

## What works today

| Feature | Status |
| --- | --- |
| `[Feature("METHOD /path")]` declarative routing | ✅ |
| Runtime assembly-scan fallback | ✅ |
| Static handlers with auto-binding (body / route / query / DI / `CancellationToken`) | ✅ |
| `[Filter<T>]` per-feature endpoint filters | ✅ |
| DataAnnotations validation, including positional records and model-level validation | ✅ |
| `ISliceValidator<T>` custom validation | ✅ |
| Problem Details for validation errors | ✅ |
| OpenAPI tag inference from namespace | ✅ |
| Zero NuGet dependencies in `Slice.Core` | ✅ |
| Source Generator for AOT (no startup reflection) | ✅ experimental |
| AWS Lambda adapter (`Slice.Lambda`) | ✅ experimental |
| Test host pattern (`Slice.TestHost`) | ✅ experimental |
| Cloudflare Workers adapter (`Slice.Workers`) | ✅ experimental (in-process dispatch; WASI publish in v1.6+) |
| CLI scaffolding (`slice new feature User`) | ✅ experimental |

## Source generator, adapters, and roadmap

Slice keeps `Slice.Core` dependency-free, while `Slice.SourceGenerator` is a separate Roslyn analyzer/generator project. Consumers can choose between the reflection fallback (`AddSlice()` / `MapSlices()`) and generated registrations (`AddSliceGenerated()` / `MapSlicesGenerated()`).

### Source Generator for full AOT

**Status:** implemented experimentally in `src/Slice.SourceGenerator` and referenced by the sample as an analyzer.

Slice's startup reflection only does mechanical setup — scan for `[Feature]` attributes, build typed delegates, and attach filters/metadata. The generator replaces that with emitted registrations:

```csharp
// Simplified generated shape: SliceRegistrations.g.cs
public static IEndpointRouteBuilder MapSlicesGenerated(this IEndpointRouteBuilder app)
{
    app.MapMethods(
            "/users",
            new[] { "POST" },
            new Func<CreateUser.Request, IUserStore, CancellationToken, Task<IResult>>(CreateUser.Handle))
        .AddEndpointFilterFactory(DataAnnotationsValidationFilter.CreateFilterFactory)
        .WithTags("Users")
        .WithName("Users.CreateUser");

    app.MapMethods(
            "/users/{id:guid}",
            new[] { "DELETE" },
            new Func<Guid, IUserStore, CancellationToken, Task<IResult>>(DeleteUser.Handle))
        .AddEndpointFilterFactory(DataAnnotationsValidationFilter.CreateFilterFactory)
        .AddEndpointFilter<RequestLoggingFilter>()
        .AddEndpointFilter<RequireApiKeyFilter>()
        .WithTags("Users")
        .WithName("Users.DeleteUser");

    // ... one explicit line per [Feature] class
    return app;
}
```

Result: zero reflection at startup, trimmer/AOT happy, near-zero cold start.

For compatibility, `AddSlice()` / `MapSlices()` still exist as the runtime-discovery fallback. For AOT/trimming, import `Slice.Generated` and call the generated methods directly.

### AWS Lambda adapter

**Status:** implemented experimentally in `src/Slice.Lambda`, with a reference app in `samples/Slice.LambdaSample`.

`Slice.Lambda` is a thin adapter over `Amazon.Lambda.AspNetCoreServer.Hosting`. `UseSliceLambda()` delegates to `AddAWSLambdaHosting()`, whose hosting package detects the Lambda environment; locally, the same binary runs on Kestrel.

```csharp
using Slice.Generated;
using Slice.Lambda;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSliceGenerated();
builder.UseSliceLambda();

var app = builder.Build();

app.MapSlicesGenerated();

await app.RunOnLambdaAsync();
```

Deploy the sample with the Lambda .NET tooling (`dotnet lambda package`) for the target runtime identifier, such as `linux-x64` or `linux-arm64`.

### Cloudflare Workers adapter (experimental)

**Status:** In-process dispatch and componentize-dotnet WASI publish are implemented experimentally, with a reference app in `samples/Slice.WorkersSample`.

`Slice.Workers` is an ASP.NET-independent satellite that routes `[Feature]` handlers through a lightweight `WorkerRouteTable` built at startup by the source generator. The same `[Feature]` classes work without modification; features that return `IResult` / `Task<IResult>` (ASP.NET-specific) are excluded automatically and the generator emits a `SLICE008` info diagnostic.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Slice.Generated;
using Slice.Workers;
using Slice.WorkersSample.Services;

var builder = WorkerHost.CreateBuilder();

// Source-generated route registration — no manual wiring needed.
builder.AddSliceGenerated();

builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();
await app.DispatchAsync(request); // in-process use; or Run() for WASI stdin/stdout IPC
```

**In-process dispatch (works today):**

```pwsh
dotnet run --project samples/Slice.WorkersSample -- --probe /health
# [probe] dispatching GET /health  →  status=200
# [probe] dispatching POST /echo {"message":"hello"}  →  status=200
# [probe] dispatching POST /echo {"message":""}  →  status=400  (DataAnnotations validation)
```

**WASI publish / local Worker dev:**

```pwsh
dotnet publish samples\Slice.WorkersSample -r wasi-wasm -c Release
cd samples\Slice.WorkersSample\worker
npm install
npm run build
npm run dev
```

The `wasi-experimental` workload (Mono-based, WASI Preview 1) is not supported in .NET 10. The sample uses [componentize-dotnet](https://github.com/bytecodealliance/componentize-dotnet) to emit a WASI 0.2 component, then `@bytecodealliance/jco` + `@bytecodealliance/preview2-shim` for the Cloudflare JavaScript bridge.

### Test host pattern

**Status:** implemented experimentally in `src/Slice.TestHost`, with a reference app in `samples/Slice.TestHostSample`.

`Slice.TestHost` isolates the `Microsoft.AspNetCore.Mvc.Testing` dependency outside `Slice.Core`. Because handlers are static and take dependencies as parameters, tests can swap DI registrations without changing application code:

```csharp
await using var host = SliceTestHost.Create<Program>(svc =>
{
    svc.Replace<IUserStore>(new InMemoryUserStore(TimeProvider.System));   // mock real infra
});
var resp = await host.Client.PostAsJsonAsync("/users", new { name = "Alice", email = "a@b.c" });
```

The same `app` instance runs in prod and in tests — Zelt's stated design goal — because Slice doesn't hide it.

### CLI scaffolding

**Status:** implemented experimentally in `tools/Slice.Cli`.

The CLI is packaged as a local .NET tool command named `slice`. It scaffolds feature and filter files into an existing Slice app:

```bash
slice new feature CreateOrder --method POST --route /orders
slice new feature GetProductDetail --method GET
slice new filter RequireApiKeyFilter
```

`slice new feature` detects the target project, reads `<RootNamespace>`, infers the feature group from common verb prefixes (`CreateUser` -> `Users`, `ListOrders` -> `Orders`, `GetProductDetail` -> `Products`), and writes to `Features/<Group>/<FeatureName>.cs`. `POST`, `PUT`, and `PATCH` templates include an empty `Request`; `GET` and `DELETE` templates do not. Pass `--project` when running outside the project directory and `--force` to overwrite an existing file.

---

The remaining deferred items are the "if I had another afternoon" pile, not "things I'm not sure how to build." The surface area is small on purpose so these can be added without breaking changes.

## Why "Vertical Slice + AOT/serverless"?

Vertical Slice and serverless are structurally aligned:

- Vertical Slice says: *"1 feature = 1 self-contained slice of code."*
- Serverless says: *"1 function = 1 deployable unit."*

If you align the slice boundary with the function boundary, you get:

- **Smaller per-function AOT binaries** — trimming is more effective because each function only references what it actually uses.
- **Faster cold starts** — less code to load and JIT (or none, with AOT).
- **Same app shape, multiple deployment modes** — run as a Kestrel server in development, host the same app on AWS Lambda today, and keep the path open for finer-grained function-per-feature deployment.

Slice's static-handler shape is designed to make the function-per-feature mapping straightforward, even though the current Lambda adapter hosts the ASP.NET Core app as a Lambda entry point.

## Build & run

```bash
dotnet build
dotnet run --project samples/Slice.Sample
```

Then:

```bash
curl http://localhost:5099/health
curl -X POST http://localhost:5099/users \
  -H "Content-Type: application/json" \
  -d '{"name":"Alice","email":"alice@example.com"}'
curl -X DELETE http://localhost:5099/users/{id} -H "X-API-Key: secret"
```

## License

MIT. Built in an afternoon as a design exploration — see the conversation that produced it.
