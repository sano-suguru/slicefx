# Product direction

SliceFx should stay focused on a simple product promise: one feature file per endpoint, portable across ASP.NET-hosted apps, serverless functions, and WASI hosts without changing the feature code. Authors keep the standard ASP.NET Core model, write one explicit feature file per endpoint, and let SliceFx generate the surrounding API contracts and tooling from that shape. The public story should be SliceFx's own: portable .NET API features with generated contracts.

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
- `SliceFx.Wasi` proves that the same feature shape can run outside ASP.NET when the handler signature and return type are portable.
- `slicefx routes` reports whether discovered features are `portable`, `partial`, or `aspnet-only` for WASI-style dispatch, and can export route metadata as JSON.
- `slicefx client csharp` generates a typed `HttpClient` wrapper for portable and partial routes, giving Blazor and other .NET clients a generated-contract starting point.
- `SliceFx.Lambda`, `SliceFx.TestHost`, WASI, and the CLI are satellite packages so optional dependencies stay out of `SliceFx.Core`.

## Typed-client and runtime references

These ideas are useful references, not the public authoring model. SliceFx should lead with standard .NET APIs plus generated contracts instead of naming other frameworks or specific deployment vendors in the product promise.

- **Small routing surface.** Keep feature registration mechanical and easy to inspect. Avoid a second fluent router unless it solves a clear .NET-specific problem.
- **Runtime portability.** Treat WASI/fetch-style dispatch as the lightweight runtime proving ground. A portable feature should not need ASP.NET-only response types.
- **Middleware at the edge of the feature.** Keep using endpoint filters instead of inventing a mediator pipeline. WASI can support only the filter concepts that make sense without ASP.NET.
- **Simple request/response primitives.** `WasiRequest`, `WasiResponse`, `SliceResult`, POCOs, `Task<T>`, and `ValueTask<T>` should remain the portable path.
- **Typed clients from routes.** Server route metadata should turn into client entrypoints. SliceFx should answer that in .NET by generating C# typed clients for Blazor/.NET consumers, and later TypeScript clients for browser/edge consumers.

## Per-feature deployment references

These ideas are useful deployment references, not a platform compatibility claim. The feature boundary should be visible to tooling and can become a deployment boundary where the runtime supports it. That does not mean file-system routing is mandatory, or that every runtime target emits one independent binary or WASM component per feature.

The current value is generated metadata, compatibility visibility, hosted deployment manifests, and HTTP API v2 Lambda deployment without changing the app model. Hosted Lambda uses one ASP.NET-hosted entry point. Function-per-feature Lambda emits one NativeAOT artifact per eligible feature so binary size and cold-start isolation can be validated at the feature boundary.

- **Convention-first project shape.** `Features/<Group>/<Feature>.cs` should stay easy to scaffold and navigate.
- **Generated route metadata.** The source generator is the natural place to emit a route manifest for tooling, docs, compatibility checks, and future deployment output.
- **Deploy target awareness.** Tooling should be able to explain whether a feature is ASP.NET-only, WASI-compatible, or potentially function-per-feature compatible.
- **One slice as a deployment boundary.** Function-per-feature Lambda deployment is available as an explicit HTTP API v2 MVP. The default Lambda path remains `SliceFx.Lambda`, which hosts the ASP.NET app as one entry point.

## Generated route metadata

The generated route manifest is the shared metadata seam for client generation, deployment tooling, and compatibility checks. It is emitted into the `SliceFx` namespace by the source generator and contains:

- HTTP method and route pattern.
- Feature type, inferred tag, endpoint name, and summary.
- Request type, return type, and handler parameter names.
- Referenced filter type names.
- Portability status using `portable`, `partial`, or `aspnet-only`.
- Lambda function-per-feature eligibility plus generated handler and artifact metadata when available.

The manifest is deliberately string-based. Deployment tools, docs, and CLI commands can read route shape and compatibility without adding runtime dependencies to `SliceFx.Core`. The CLI route catalog should prefer generated metadata from built project outputs, then fall back to source scanning only for unbuilt projects. Route listing, compatibility reporting, typed client generation, OpenAPI, and future deployment checks should share this contract.

## CLI-generated tooling

The CLI is the first user-visible surface for this direction:

```bash
slicefx routes
slicefx routes --format json
slicefx client csharp --output SliceApiClient.g.cs
slicefx client typescript --output slice-api-client.ts
slicefx openapi --output openapi.json
```

`slicefx routes` makes portability visible at the slice boundary:

- `portable` means the route avoids ASP.NET-specific return types and can be considered for WASI-style dispatch.
- `partial` means the route shape is portable, but some attached behavior such as endpoint filters is ASP.NET-only today.
- `aspnet-only` means the route intentionally depends on ASP.NET concepts such as `IResult`.

`slicefx client csharp` and `slicefx client typescript` generate typed clients for portable and partial routes: write the server feature once, then let tooling produce the client entrypoint instead of manually maintaining endpoint strings and DTO wiring.

`slicefx openapi` projects the same manifest into OpenAPI JSON for portable tooling without starting an ASP.NET host. The ASP.NET Core `AddOpenApi` / `MapOpenApi` document remains the authoritative hosted-app document; the CLI output is stamped as a manifest projection and must not invent metadata beyond the manifest.

`slicefx new wasi-cloudflare` scaffolds Cloudflare Workers deployment glue for the WASI path. The primary WASI sample also deploys natively to Fermyon Cloud / Spin because it emits a standard `wasi:http/incoming-handler` component. Runtime docs should present this as wasi:http portability first, with Cloudflare glue and Spin deployment as target-specific details. Keep the support language precise: `SliceFx.Wasi` is experimental 0.x package surface, while the upstream WASI build/transpile stack is preview/unstable and can break independently of SliceFx.

## Non-goals

- Do not clone another framework's fluent routing API as a parallel authoring model by default.
- Do not replace ASP.NET Minimal APIs with a custom HTTP stack for the main runtime.
- Do not add mediator-style abstractions such as `IMediator` or `IPipelineBehavior`.
- Do not add package dependencies to `SliceFx.Core`.
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

For WASI-specific responses, use `SliceResult` or `WasiResponse`. The generator currently excludes ASP.NET-specific `IResult` features from WASI route tables with `SLICE020`.

## WASI/fetch-style direction

`SliceFx.Wasi` is the active experiment for a lightweight wasi:http runtime. Its minimal dispatch surface is `WasiRequest` in and `WasiResponse` out, with `SliceResult` helpers for common responses. The route table should stay generated and deterministic; ASP.NET endpoint filters should not be assumed to run in the WASI path.

Near-term WASI work should focus on better manifest-driven compatibility reporting and source-generated JSON/validation coverage before introducing another public routing API.

## Deployment direction

`slicefx manifest aws-lambda` (CLI) generates an AWS SAM `template.yaml` from the source-generated route manifest. The default hosted mode emits one `AWS::Serverless::Function` for the ASP.NET-hosted Slice app and one API Gateway `HttpApi` event per `[Feature]`. ASP.NET route constraints are automatically stripped for API Gateway. The default runtime is `provided.al2023` (NativeAOT / self-contained, handler `bootstrap`).

`--mode function-per-feature` is implemented on the `SliceFx.Lambda.FunctionPerFeature` satellite. The source generator emits route-scoped handler types when the assembly opts in with `[assembly: LambdaFunctionPerFeature]`; the SAM template emits one `AWS::Serverless::Function` per eligible feature; and `slicefx package aws-lambda --mode function-per-feature --rid <RID>` publishes one NativeAOT custom-runtime artifact per eligible feature. Package wrappers generate route-local JSON metadata and inspect NativeAOT mstat/map output so sibling feature roots, validators, generated app-wide registration surfaces, and hosted ASP.NET bootstrap do not silently enter a per-feature artifact. Shared artifacts belong to hosted Lambda, not function-per-feature packaging. PR CI gates `linux-x64`; `linux-arm64` is covered by a scheduled/manual workflow because ARM64 runner availability varies.

### Lambda function-per-feature MVP scope

The first function-per-feature Lambda mode is intentionally narrower than ASP.NET-hosted Lambda. It targets API Gateway HTTP API v2 and supports route parameters, query parameters, JSON request bodies, DI services, and `CancellationToken`. Supported return shapes start with POCOs, `Task<T>`, and `ValueTask<T>`; additional result helpers can be added only if they do not recreate ASP.NET's endpoint pipeline.

Function-per-feature Lambda and WASI should share the same portable binding contract: missing nullable query parameters bind `null`, missing non-nullable query parameters fail with `400 Bad Request`, and invalid present values fail with `400 Bad Request`. Typed `null` returns are serialized as JSON `null`; absence-like responses should use explicit result helpers on the paths that support them.

The MVP excludes features that return `IResult`, depend on endpoint filters, require reflection-bound validation, use unsupported route parameter types, or use JSON body/response roots that cannot be emitted into a route-local NativeAOT wrapper context. ASP.NET-specific middleware behavior such as auth, CORS, or Problem Details customization remains outside the compile-time contract. API Gateway REST v1, ALB, non-HTTP triggers, and per-feature WASM components stay out of the MVP. Reuse the existing WASI-style binding concepts where practical so portable feature rules converge instead of splitting by host.

Cloudflare WASI function-per-feature deployment (one WASM component per feature) requires per-feature NativeAOT compilation and is blocked on `componentize-dotnet` multi-component build support. The current `SliceFx.Wasi` model (one WASM component, all routes dispatched in-process) is the practical deployment target until that tooling matures.

Even when the build tooling matures, the benefit of per-feature WASI deployment is less clear than for function hosts with per-function scaling and billing knobs. The main remaining benefit would be independent deployment per feature (deploying one route without affecting others). That benefit alone may not justify the build complexity unless `componentize-dotnet` makes per-feature compilation straightforward. Until a deliberate design changes this, docs and CLI help should describe WASI deployment as single-component dispatch and should not advertise WASI per-feature packaging.

## Directional focus

- Keep generated route metadata as the shared contract seam for route listing, typed clients, OpenAPI projections, portability reporting, and deployment tooling.
- Keep typed clients manifest-driven and portable-first across .NET and TypeScript without making ASP.NET-only routes look portable.
- Keep deployment tooling explicit about target capability gaps and route exclusions instead of implying every feature can run on every host.
- Keep WASI/fetch-style dispatch focused on the portable `WasiRequest` -> `WasiResponse` path rather than introducing a second public routing model.
- Treat independent per-feature binaries as a long-term deployment optimization, not the core product promise.
