# Slice

[![CI](https://github.com/sano-suguru/slice/actions/workflows/ci.yml/badge.svg)](https://github.com/sano-suguru/slice/actions/workflows/ci.yml)
[![Perf (nightly)](https://github.com/sano-suguru/slice/actions/workflows/perf.yml/badge.svg)](https://github.com/sano-suguru/slice/actions/workflows/perf.yml)
[![Pages](https://github.com/sano-suguru/slice/actions/workflows/pages.yml/badge.svg)](https://github.com/sano-suguru/slice/actions/workflows/pages.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> One feature file per endpoint. Generated ASP.NET Core registration, checks, clients, and portability hints.

Website: <https://sano-suguru.github.io/slice/>

**Slice** is an experimental .NET framework for teams that like ASP.NET Core Minimal APIs but do not want route strings, DTOs, validation, filters, clients, and deployment checks to drift apart. A feature is one static class with its request, response, handler, validation, and filters in one place. The generator emits standard Minimal API registrations plus a route manifest for tooling, AOT-friendly startup, Lambda experiments, and WASI/WebAssembly dispatch.

Curious about the design choices? See **[Design decisions FAQ](docs/design-decisions.md)** and **[Production readiness criteria](docs/production-readiness.md)**.

## Why use Slice?

| Need | Slice provides |
| --- | --- |
| Endpoint code that is easy to review | One feature file per endpoint: request, response, handler, validation, and filters stay together. |
| Less hand-synced API glue | `AddSlice()` / `MapSlices()`, route metadata, and typed clients are generated from the same feature definitions. |
| Standard ASP.NET Core behavior | Minimal API binding, DI, endpoint filters, DataAnnotations, OpenAPI compatibility, and `IResult` remain available. |
| Native AOT-friendly startup | Generated `MapMethods` calls avoid startup route scanning; `Slice.Core` has no `PackageReference` entries and only uses the `Microsoft.AspNetCore.App` framework reference. |
| Early portability feedback | `slice routes` classifies each endpoint as `portable`, `partial`, or `aspnet-only`; Lambda and wasi:http adapters are optional. |

Slice is not a replacement for ASP.NET Core. It is a generated vertical-slice layer around Minimal APIs for teams that want explicit feature files, generated contracts, and portability checks without adopting a mediator stack or custom endpoint pipeline.

Slice compiles down to standard `WebApplication.MapMethods` calls — removing the source generator reference and expanding the generated output in place is the full exit path. For teams already on FastEndpoints or similar, Slice fills a different niche: compile-time portability classification across ASP.NET, Lambda, and wasi:http, not a richer filter and pipeline ecosystem.

### Latest benchmark results

![Latest source generator benchmark results](docs/perf/latest.svg)

Each endpoint is a static feature file: request, response, validation, filters, and handler stay together. The source generator turns those features into ASP.NET registrations, route metadata for tooling, Lambda handlers, or wasi:http dispatch where the handler shape is portable.

```bash
dotnet run --project samples/Slice.Sample
curl http://localhost:5099/health
```

## Project status

Slice is pre-1.0 experimental software. Preview packages use `0.x` versions until the API is intentionally stabilized.

WASI support (`Slice.Wasi`) depends on [componentize-dotnet](https://github.com/bytecodealliance/componentize-dotnet), a preview package targeting the WASI Preview 2 / `wasi:http@0.2` interface. Native WASI publish requires Linux x64 or Windows x64; macOS requires a Docker cross-build. The WASI toolchain is actively evolving and may introduce breaking changes between preview releases. Treat any project targeting `Slice.Wasi` as experimental until the upstream tooling stabilizes.

| Package | Purpose |
| --- | --- |
| `Slice.Core` | Core runtime: `[Feature]`, `[Filter<T>]`, validation, and endpoint filters. |
| `Slice.SourceGenerator` | AOT-friendly generated registrations and route metadata. |
| `Slice.Lambda` | ASP.NET-hosted AWS Lambda adapter. |
| `Slice.Lambda.PerFunction` | Experimental HTTP API v2 per-feature Lambda handlers. |
| `Slice.TestHost` | In-process test host helpers. |
| `Slice.Wasi` | ASP.NET-independent wasi:http dispatch. |
| `Slice.Cli` | Scaffolding, route inspection, AWS SAM manifest/package helpers, and typed client generation. |

Minimal ASP.NET Core apps need the core runtime and source generator:

```bash
dotnet add package Slice.Core --version 0.1.0-preview.1
dotnet add package Slice.SourceGenerator --version 0.1.0-preview.1
```

## Hello, Slice

`Program.cs`:

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();

var app = builder.Build();

app.MapSlices();
app.Run();
```

`Features/Users/CreateUser.cs`:

```csharp
namespace Slice.Sample.Features.Users;

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

The generator discovers `[Feature]` classes, emits `AddSlice()` / `MapSlices()`, wires Minimal API binding, and attaches validation. Prefer plain response records for portable endpoints. Use `IResult` when the feature intentionally needs ASP.NET-specific response helpers such as `Results.NotFound()` or `Results.NoContent()`.

## Filters and validation

Feature filters are standard ASP.NET Core `IEndpointFilter` types:

```csharp
[Feature("DELETE /users/{id:guid}", Summary = "Delete a user")]
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

`AddSlice()` registers referenced filters as scoped services, and `[Filter<T>]` applies them in declaration order. DataAnnotations validation runs before feature filters. For rules that need code, attach `SliceValidatorFilter<TRequest>` and register an `ISliceValidator<TRequest>` manually in DI.

Read more:

- [Filter declarations](docs/guides/filter-declarations.md)
- [Filter configuration](docs/patterns/filter-configuration.md)
- [Return-type guidance](docs/guides/return-types.md)

## What works today

| Feature | Status |
| --- | --- |
| `[Feature("METHOD /path")]` declarative routing | Implemented |
| Source-generated `AddSlice()` / `MapSlices()` | Implemented |
| Static handlers with body / route / query / DI / `CancellationToken` binding | Implemented |
| DataAnnotations validation, including positional records and model-level validation | Implemented |
| `ISliceValidator<T>` custom validation | Implemented |
| `[Filter<T>]` endpoint filters | Implemented |
| Route metadata manifest | Experimental |
| `slice routes` portability classification | Experimental |
| `slice client csharp` typed client generation | Experimental |
| `slice client typescript` typed fetch client generation | Experimental |
| AWS SAM manifest generation | Experimental |
| ASP.NET-hosted Lambda adapter | Experimental |
| Per-feature Lambda handlers | Experimental HTTP API v2 MVP |
| TestHost helper | Experimental |
| WASI adapter | Experimental in-process wasi:http dispatch |

## Portability

The source generator classifies each feature endpoint at build time. `slice routes` reports the result; the same data drives typed-client generation, WASI route tables, and Lambda per-feature eligibility.

| Class | Meaning |
| --- | --- |
| `portable` | Returns a plain record or void. Eligible for typed-client generation, WASI dispatch, and per-feature Lambda. |
| `partial` | Portable handler shape, but attached non-validator filters are ASP.NET-only today. |
| `aspnet-only` | Returns `IResult` or uses ASP.NET-specific behavior. The full Minimal API feature set is available. |

Mixing all three classes in the same project is the expected pattern. `aspnet-only` features are standard Minimal API endpoints with the complete ASP.NET ecosystem available — they are not penalized or degraded. The classification tells tooling where a feature can run, not whether it is well-written.

### WASI and edge are optional

You do not need WASI or edge hosting to use Slice. The default path is still a normal ASP.NET Core app.

**Edge** usually means running code closer to users on platforms such as Cloudflare Workers or Fermyon Spin instead of only in one central server region. **WASI** is a standards-based way to package server-side code as a WebAssembly component that those hosts can run. In Slice, WASI support proves the portability story: if a feature returns plain request/response records and avoids ASP.NET-only response helpers, the same feature shape can be dispatched outside ASP.NET through a generated route table and `Slice.Wasi`.

That path is intentionally experimental. `Slice.Wasi` depends on preview tooling, has stricter JSON and validation rules, and does not run arbitrary ASP.NET endpoint filters. The practical benefit today is visibility: `slice routes` tells you which endpoints are portable, which are partially portable, and which intentionally stay ASP.NET-only.

## OpenAPI

Slice endpoints work with ASP.NET Core's standard OpenAPI support out of the box — add `Microsoft.AspNetCore.OpenApi` and call `app.MapOpenApi()` after `app.MapSlices()`. The Slice route manifest is a separate build-time artifact for portability classification and client generation; it complements rather than replaces the OpenAPI document.

## Tooling and adapters

| Topic | Details |
| --- | --- |
| Source generator and route manifest | [docs/source-generator.md](docs/source-generator.md) |
| CLI commands | [docs/cli.md](docs/cli.md) |
| Lambda hosting and per-feature Lambda | [docs/lambda.md](docs/lambda.md) |
| WASI deploy path | [samples/Slice.WasiSample/README.md](samples/Slice.WasiSample/README.md) |
| Platform abstraction and DI swap patterns | [docs/patterns/platform-abstraction.md](docs/patterns/platform-abstraction.md) |
| Design decisions FAQ | [docs/design-decisions.md](docs/design-decisions.md) |
| Product direction | [docs/product-direction.md](docs/product-direction.md) |
| Production readiness | [docs/production-readiness.md](docs/production-readiness.md) |

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
