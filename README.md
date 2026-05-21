# Slice

> Minimal API with feature files and generated contracts — not a replacement framework.

Website: <https://sano-suguru.github.io/slice/>

Slice is an experimental .NET web framework built on ASP.NET Core Minimal APIs. Author each endpoint as one explicit feature file, keep the standard ASP.NET Core programming model, and let Slice generate registrations, route metadata, compatibility reports, and typed clients around that shape.

```bash
dotnet run --project samples/Slice.Sample
slice routes --project samples/Slice.Sample/Slice.Sample.csproj
```

The goal is not to be a bigger FastEndpoints or a parallel web stack. Slice is a small, generated, vertical-slice API layer for teams that want Minimal API behavior, typed clients, and portability checks without hand-maintained endpoint strings.

It focuses on three product ideas:

- **Feature-shaped APIs** — one static class per endpoint, with request, response, validation, and handler kept together.
- **Generated API contracts** — route metadata, compatibility reports, and typed clients come from the same feature files.
- **Standard by default** — keep Minimal API binding, DI, endpoint filters, DataAnnotations, and `IResult` when you need ASP.NET-specific responses.

## Who is it for?

Slice is strongest when you want a small .NET API to stay understandable as it grows:

| Team | Why Slice helps |
| --- | --- |
| Small API and product teams | Each endpoint is owned as one file, so request, response, validation, filters, and handler do not drift across folders. |
| Blazor and .NET client teams | `slice client csharp` can generate typed `HttpClient` wrappers from server features instead of hand-maintaining route strings and DTO wiring. |
| AOT, serverless, and Workers-minded teams | `slice routes` makes portability visible early, while ASP.NET Core remains the primary runtime. |
| Framework-light .NET teams | Slice keeps ASP.NET Core Minimal API binding and endpoint filters instead of introducing a mediator pipeline or required validation stack. |

`Slice.Core` has zero NuGet package dependencies; the optional source generator lives in a separate project and uses Roslyn packages.

## Positioning

Slice does not replace ASP.NET Core. It adds compile-time structure and tooling around Minimal APIs without introducing a second runtime model.

| Framework | Authoring model | Execution model | AOT fit | Contract tooling |
| --- | --- | --- | --- | --- |
| ASP.NET Core MVC | Controllers and actions split by layer | Built-in MVC pipeline | Partial; controller discovery and model binding need trimming care | Not built in |
| Minimal APIs | Flexible route declarations | Standard Minimal API pipeline | Very high | Not built in |
| FastEndpoints | Endpoint/REPR-style classes | Custom endpoint abstraction on ASP.NET Core | High | Available through its ecosystem |
| Slice | One feature file per endpoint | Source-generated Minimal API registrations | Very high | Built in: route manifest, `slice routes`, `slice client csharp` |

Slice combines three things:

1. **Feature locality with Minimal API behavior.** A feature keeps request, response, validation, filters, and handler together, but the generated code maps directly to ASP.NET Core Minimal APIs.
2. **Build-time guardrails.** The source generator reports diagnostics such as `SLICE001` and `SLICE002` for invalid feature shape, including a missing or non-`public static` `Handle` method.
3. **Tooling generated from the server shape.** The same feature metadata powers route inspection, portability reports for Workers-style dispatch, and typed C# clients for Blazor or other .NET clients.

Slice is still pre-1.0. For large established monoliths, use Minimal APIs, MVC, or a mature endpoint framework unless Slice's generated contracts and portability checks are the reason for adopting it.

## Project status

Slice is being prepared for OSS preview releases. The core API, source generator, serverless adapters, test host, and CLI exist, but the project should still be treated as experimental. Preview packages use `0.x` versions until the API is intentionally stabilized for a future `1.0`.

Packages are split so optional dependencies stay out of `Slice.Core`:

| Package | Purpose |
| --- | --- |
| `Slice.Core` | Core runtime: `[Feature]`, `[Filter<T>]`, validation attributes, and endpoint filters. |
| `Slice.SourceGenerator` | Required AOT-friendly generated registrations for Slice features. |
| `Slice.Lambda` | AWS Lambda hosting adapter. |
| `Slice.TestHost` | In-process test host helpers. |
| `Slice.Workers` | ASP.NET-independent Workers/WASI adapter. |
| `Slice.Cli` | Local scaffolding, route inspection, compatibility reporting, and typed client generation. |

Minimal ASP.NET Core apps need both the core runtime and the source generator:

```bash
dotnet add package Slice.Core --version 0.1.0-preview.1
dotnet add package Slice.SourceGenerator --version 0.1.0-preview.1
```

## Design principles

- **Vertical Slice first.** Each HTTP endpoint is one self-contained file. Request, response, handler, validator, filters — all together.
- **Explicit over magic.** Routes are declared in `[Feature("METHOD /path")]`; file and namespace conventions help tooling, but they do not hide the HTTP contract.
- **Static handlers.** Every `Handle` method is `static`. Dependencies come in as parameters. No instance state, no DI on the type itself, no reflection on the hot path after startup.
- **Zero new abstractions.** Slice doesn't invent `IPipelineBehavior<TReq, TRes>` or `IMediator` — it leans on ASP.NET Core's existing `IEndpointFilter`. Less to learn, less to maintain.
- **Declarative everything.** `[Feature]`, `[Filter<T>]`. The source generator reads these and registers Minimal API endpoints, validation, and filter chains automatically.
- **AOT-ready structure.** Handlers are static and receive a strongly-typed delegate. Generated registrations avoid startup reflection and are the only registration path.
- **Zero runtime framework dependencies.** Slice.Core has only one reference: `Microsoft.AspNetCore.App`. No FluentValidation, no MediatR, no AutoMapper. Roslyn dependencies are isolated to `Slice.SourceGenerator`.
- **Convention-first, not convention-only.** Feature files live naturally under `Features/<Group>`, and generated metadata should support tooling and deployment, but the public authoring model stays explicit and .NET-native.
- **Portable where practical.** ASP.NET Minimal API remains the main runtime. Workers/serverless paths reuse the same feature shape when the return type and dependencies are portable.
- **Tooling from slices.** Route inspection, compatibility checks, metadata export, and typed clients should be generated from the slice shape instead of hand-maintained API contracts.

## Development discipline

Changes are guarded by CI, a PR checklist, and a small set of project invariants. See [CONTRIBUTING.md](CONTRIBUTING.md) for the Definition of Done and local verification commands.

For release preparation, see [docs/oss-release-checklist.md](docs/oss-release-checklist.md).

For the current product direction, see [docs/product-direction.md](docs/product-direction.md).

## Hello, Slice

```csharp
// Program.cs — AOT-friendly generated bootstrap
var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddSlice();
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();
var app = builder.Build();
app.MapSlices();   // generated registrations for every [Feature]
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

    public static async Task<Response> Handle(Request req, IUserStore store, CancellationToken ct)
    {
        var user = await store.AddAsync(req.Name, req.Email, ct);
        return new Response(user.Id, user.Name, user.Email, user.CreatedAt);
    }
}
```

That's it. The framework:
1. Discovers `CreateUser` via the `[Feature]` attribute at compile time when the source generator is enabled.
2. Registers `POST /users` against the static `Handle` method.
3. Lets Minimal API auto-bind the body (`Request`), services (`IUserStore`), and the cancellation token.
4. Validates request DTOs via DataAnnotations attributes — including record positional parameters, type-level attributes, and `IValidatableObject`.
5. Returns Problem Details if validation fails.

Prefer returning a `Response` record for the default Slice style. Use `IResult` only when the feature intentionally needs ASP.NET-specific response helpers such as `Results.NotFound()` or `Results.Created(...)`.

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

Filters are just `IEndpointFilter` implementations — no special interface. `AddSlice()` auto-registers them in DI, and `[Filter<T>]` applies them in declaration order (outermost wraps innermost).

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
    public record Response(string Echo);

    public static Response Handle(Request req)
        => new(req.Message);
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
| Source-generated `AddSlice()` / `MapSlices()` registrations | ✅ |
| Static handlers with auto-binding (body / route / query / DI / `CancellationToken`) | ✅ |
| `[Filter<T>]` per-feature endpoint filters | ✅ |
| DataAnnotations validation, including positional records and model-level validation | ✅ |
| `ISliceValidator<T>` custom validation | ✅ |
| Problem Details for validation errors | ✅ |
| OpenAPI tag inference from namespace | ✅ |
| Zero NuGet dependencies in `Slice.Core` | ✅ |
| Compile-time diagnostics such as `SLICE001` and `SLICE002` for invalid feature shape | ✅ |
| Source Generator for AOT (no startup reflection) | ✅ experimental |
| AWS Lambda adapter (`Slice.Lambda`) | ✅ experimental |
| Test host pattern (`Slice.TestHost`) | ✅ experimental |
| Cloudflare Workers adapter (`Slice.Workers`) | ✅ experimental (in-process dispatch; WASI publish path) |
| CLI scaffolding (`slice new feature User`) | ✅ experimental |
| Generated route metadata manifest | ✅ experimental |
| Typed C# client generation (`slice client csharp`) | ✅ experimental |

## Source generator, adapters, and roadmap

Slice keeps `Slice.Core` dependency-free, while `Slice.SourceGenerator` is a separate Roslyn analyzer/generator project. The source generator emits `AddSlice()` / `MapSlices()` — the single registration path, AOT-friendly by design.

### Source Generator for full AOT

**Status:** implemented experimentally in `src/Slice.SourceGenerator` and referenced by the sample as an analyzer.

The generator emits one explicit registration per `[Feature]` class. Feature assemblies expose generated module helpers, and the host assembly emits the user-facing `AddSlice()` / `MapSlices()` extensions that aggregate its own features plus referenced Slice feature assemblies.

```csharp
// Simplified generated shape: SliceRegistrations.g.cs
public static IEndpointRouteBuilder MapSlices(this IEndpointRouteBuilder app)
{
    app.MapMethods(
            "/users",
            new[] { "POST" },
            new Func<CreateUser.Request, IUserStore, CancellationToken, Task<CreateUser.Response>>(CreateUser.Handle))
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

Result: zero reflection at startup and a trimmer-friendly registration path.

The source generator also emits route metadata for tooling and deployment experiments, including empty manifests for projects that do not define features yet. The metadata contains method, pattern, feature type, tag, endpoint name, summary, request type, return type, handler parameters, filters, and portability status (`portable`, `partial`, or `aspnet-only`). It is intentionally string-based so tooling can consume route shape without adding dependencies to `Slice.Core`.

For multi-assembly apps, reference `Slice.SourceGenerator` from each feature assembly and from the host. Class library projects default to generated module helpers only; executable hosts default to the public extension surface and aggregate directly referenced Slice modules. Set the MSBuild property `SliceRole` to `Host`, `Feature`, or `Both` only when you need to override that default.

Hosts can control referenced module aggregation with MSBuild properties:

```xml
<PropertyGroup>
  <!-- Default: true. Set false to map only features compiled into this project. -->
  <SliceAggregateReferences>false</SliceAggregateReferences>

  <!-- Optional allow-list. When set, only these referenced assembly names are aggregated. -->
  <SliceReferencedAssemblies>FeatureLib;SharedSlices</SliceReferencedAssemblies>
</PropertyGroup>
```

The generator validates endpoint-name uniqueness across local features and aggregated referenced modules before emitting host registrations.

### AWS Lambda adapter

**Status:** implemented experimentally in `src/Slice.Lambda`, with a reference app in `samples/Slice.LambdaSample`.

`Slice.Lambda` is a thin adapter over `Amazon.Lambda.AspNetCoreServer.Hosting`. `UseSliceLambda()` delegates to `AddAWSLambdaHosting()`, whose hosting package detects the Lambda environment; locally, the same binary runs on Kestrel.

```csharp
using Slice.Lambda;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.UseSliceLambda();

var app = builder.Build();

app.MapSlices();

await app.RunOnLambdaAsync();
```

Deploy the sample with the Lambda .NET tooling (`dotnet lambda package`) for the target runtime identifier, such as `linux-x64` or `linux-arm64`.

### Cloudflare Workers adapter (experimental)

**Status:** In-process dispatch and componentize-dotnet WASI publish are implemented experimentally, with a reference app in `samples/Slice.WorkersSample`.

`Slice.Workers` is an ASP.NET-independent satellite that routes `[Feature]` handlers through a lightweight `WorkerRouteTable` built at startup by the source generator. The same `[Feature]` classes work without modification; features that return `IResult` / `Task<IResult>` (ASP.NET-specific) are excluded automatically and the generator emits a `SLICE008` info diagnostic.

Portable Workers features should return `WorkerResponse`, `SliceResult`, a POCO, `Task<T>`, or `ValueTask<T>`. ASP.NET-specific `IResult` remains the right choice for Minimal API features that rely on ASP.NET response helpers, filters, or endpoint behavior.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Slice.Workers;
using Slice.WorkersSample.Services;

var builder = WorkerHost.CreateBuilder();

// Source-generated route registration — no manual wiring needed.
builder.AddSlice();

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
slice routes
slice routes --format json
slice client csharp --output SliceApiClient.g.cs
```

`slice new feature` detects the target project, reads `<RootNamespace>`, infers the feature group from common verb prefixes (`CreateUser` -> `Users`, `ListOrders` -> `Orders`, `GetProductDetail` -> `Products`), and writes to `Features/<Group>/<FeatureName>.cs`. Generated templates return a nested `Response` record by default; `POST`, `PUT`, and `PATCH` templates also include an empty `Request`.

`slice routes` first reads source-generated route metadata from the built project output when available, including directly referenced Slice feature assemblies copied beside the app. If the project has not been built yet, it falls back to scanning `Features/**/*.cs` source files. The command reports whether each slice is `portable`, `partial`, or `aspnet-only` for Workers-style dispatch, and `--format json` exports the same route metadata for tooling. `slice client csharp` generates a typed `HttpClient` wrapper for portable and partial routes, which is useful for Blazor and other .NET clients. Pass `--project` when running outside the project directory and `--force` to overwrite an existing file.

`Features/<Group>/<Feature>.cs` is the recommended and scaffolded project shape, not a compiler-enforced file-system routing rule. The generator discovers `[Feature]` classes, so explicit attributes remain the source of truth.

## Why "Vertical Slice + AOT/serverless"?

Vertical Slice and serverless are structurally aligned:

- Vertical Slice says: *"1 feature = 1 self-contained slice of code."*
- Serverless says: *"1 function = 1 deployable unit."*

If you align the slice boundary with the function boundary, you get:

- **Smaller per-function AOT binaries** — trimming is more effective because each function only references what it actually uses.
- **Faster cold starts** — less code to load and JIT (or none, with AOT).
- **Same app shape, multiple deployment modes** — run as a Kestrel server in development, host the same app on AWS Lambda today, and keep the path open for finer-grained function-per-feature deployment.

Slice's static-handler shape is designed to make the function-per-feature mapping straightforward, even though the current Lambda adapter hosts the ASP.NET Core app as a Lambda entry point. The near-term deployment direction is full-app deployment plus generated route metadata first; function-per-feature build output should be evaluated on top of that manifest without changing the feature authoring model.

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

MIT. See [LICENSE](LICENSE).
