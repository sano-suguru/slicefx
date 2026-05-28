# SliceFx CLI

`SliceFx.Cli` is the local `slicefx` command for scaffolding, route inspection, client generation, and deployment artifacts.

## Commands

```bash
slicefx new feature CreateOrder --method POST --route /orders
slicefx new feature GetProductDetail --method GET
slicefx new filter RequireApiKeyFilter
slicefx new wasi-cloudflare

slicefx routes
slicefx routes --format json

slicefx client csharp --output SliceApiClient.g.cs
slicefx client typescript --output slice-api-client.ts

slicefx openapi --output openapi.json

slicefx manifest aws-lambda --output template.yaml
slicefx manifest aws-lambda --mode function-per-feature --output template.yaml
slicefx package aws-lambda --mode function-per-feature --rid linux-x64 --output artifacts/aws-lambda
```

Pass `--project` when running outside the project directory. Use `--force` for commands that write files and should overwrite existing output.

## Scaffolding

`slicefx new feature` detects the target project, reads `<RootNamespace>`, infers the feature group from common verb prefixes, and writes to `Features/<Group>/<FeatureName>.cs`.

Examples:

| Feature name | Default group |
| --- | --- |
| `CreateUser` | `Users` |
| `ListOrders` | `Orders` |
| `GetProductDetail` | `Products` |

Generated feature templates return a nested `Response` record by default. `POST`, `PUT`, and `PATCH` templates also include an empty `Request`.

`slicefx new filter` scaffolds an `IEndpointFilter`.

`slicefx new wasi-cloudflare` scaffolds Cloudflare Workers host files for a `SliceFx.Wasi` component into `dist/`: `shim.mjs`, `package.json`, Wrangler config, socket stubs, and module-map generation. App-specific pieces such as `IncomingHandlerImpl.cs` and a `[SliceJsonContext(SliceJsonTarget.Wasi)]` JSON context remain in the app because they depend on WIT-generated types and user DTO metadata.

The scaffold pins the Cloudflare JS tool versions and declares Node.js 22+ because the pinned Wrangler release requires it. It does not emit a lockfile, so run `npm install` the first time, review and commit the generated `package-lock.json`, then use `npm ci` for subsequent installs. The checked-in `samples/SliceFx.WasiSample/dist` directory already includes `package-lock.json` and uses `npm ci` for reproducible sample installs. The upstream WASI build/transpile toolchain remains preview/unstable even though the `SliceFx.Wasi` package API is tracked as experimental 0.x surface.

This command scaffolds single-component WASI deployment glue. It does not create per-feature WASM artifacts, and there is no `slicefx package wasi` command today.

## Route inspection

`slicefx routes` reads source-generated route metadata from the built project output when available. For referenced Slice feature assemblies, it includes only assemblies the host explicitly aggregates through generated metadata (`SliceFxReferencedAssemblies` or `SliceFxAggregateReferences=true`) and prints a stderr notice naming those assemblies. If the project has not been built yet, it falls back to scanning local `Features/**/*.cs`.

The command reports each route's portability:

| Status | Meaning |
| --- | --- |
| `portable` | The handler shape avoids ASP.NET-specific return types and can be considered for WASI-style dispatch. |
| `partial` | The route shape is portable, but some attached behavior such as endpoint filters is ASP.NET-only today. |
| `aspnet-only` | The route intentionally depends on ASP.NET concepts such as `IResult`. |

The table output includes a `SOURCE` column with the assembly that contributed the route. `--format json` exports the same route metadata for tooling, including `sourceAssemblyName`.

## Typed C# client

`slicefx client csharp` generates a typed `HttpClient` wrapper for portable and partial routes. This is useful for Blazor and other .NET clients that should not hand-maintain endpoint strings and DTO wiring.

The C# client reuses the C# contract types from the Slice handler signature; it does not emit DTO copies. A client project must reference the assembly that contains those request and response types. Nested feature DTOs such as `CreateUser.Request` and `CreateUser.Response` therefore require visibility of the feature assembly. For Blazor or shared .NET clients that should not reference the server feature assembly, put request/response records in a shared contracts project and use those non-nested types in the handler signature.

The generated class is `public partial class` so it can be extended in a sibling file. Two extension points are available: a `public {ClassName}(HttpMessageHandler handler)` constructor overload for injecting `DelegatingHandler` chains (Polly, auth headers, telemetry), and a `partial void OnRequestPreparing(HttpRequestMessage request)` hook that runs before every outgoing request.

To integrate with `IHttpClientFactory`, wire up the named client in your composition root and pass the resolved `HttpClient` to the constructor directly:

```csharp
// Registration (ASP.NET host or Blazor WASM)
builder.Services.AddHttpClient(nameof(SliceApiClient), c => c.BaseAddress = new Uri("https://api.example.com"))
    .AddHttpMessageHandler<YourDelegatingHandler>();
builder.Services.AddScoped(sp =>
    new SliceApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SliceApiClient))));
```

## Typed TypeScript client

`slicefx client typescript` generates a zero-dependency `fetch`-based TypeScript client for portable and partial routes. TypeScript interfaces are emitted for request and response record shapes where property information is available in the built project output. The schema reader honors common `System.Text.Json` metadata such as `[JsonPropertyName]`, `[JsonIgnore]`, required members, string-enum converters, and binary members represented as base64 strings.

The generated code requires only the global `fetch` API and runs in browsers, Cloudflare Workers, Node.js 18+, and Deno. The generated class accepts a `baseUrl` string and an optional `RequestInit` for default headers or credentials:

```typescript
const client = new SliceApiClient("https://api.example.com", {
  headers: { Authorization: `Bearer ${token}` }
});
const item = await client.items.getItemAsync(42);
```

`aspnet-only` routes are excluded from generated TypeScript and C# clients. Use a standard OpenAPI toolchain or a manual ASP.NET-specific client for those endpoints.

## OpenAPI manifest projection

`slicefx openapi` writes an OpenAPI JSON document from the source-generated route manifest. It is designed for CI, WASI, Lambda function-per-feature, and other cases where you want a portable contract without starting the ASP.NET host:

```bash
slicefx openapi --output openapi.json
slicefx openapi --title SliceFx.Sample --version 1.0.0 --output openapi.json
```

The document is marked with `x-slicefx-source: "manifest"`. It projects common `System.Text.Json` metadata, nullable handler parameters, and Minimal API binding names/sources from generated route metadata and build output. For hosted ASP.NET apps, the authoritative OpenAPI document should still come from `Microsoft.AspNetCore.OpenApi` via `builder.Services.AddOpenApi()` and `app.MapOpenApi()`.

By default, `slicefx openapi` includes `portable` and `partial` routes. `aspnet-only` routes are omitted because the manifest cannot safely infer `IResult` response schemas; omissions are written as warnings and included in `x-slicefx-omitted`. Pass `--include-aspnet-only` only when you want those operations emitted with incomplete schemas and explicit `x-slicefx-portability` metadata.

## AWS Lambda artifacts

`slicefx manifest aws-lambda` reads the source-generated route manifest and writes an AWS SAM `template.yaml`.

By default (`--mode hosted`), it emits one `AWS::Serverless::Function` for the ASP.NET-hosted SliceFx app and one API Gateway `HttpApi` event per `[Feature]`. All features are included because `SliceFx.Lambda` runs through ASP.NET Core hosting.

`--mode function-per-feature` emits one `AWS::Serverless::Function` per eligible generated `SliceFx.Lambda.FunctionPerFeature` handler and excludes unsupported routes with reasons. Each function points at its own NativeAOT custom-runtime artifact. ASP.NET route constraints such as `{id:guid}` are converted to API Gateway syntax such as `{id}`.

The default runtime is `provided.al2023`. Use `--runtime dotnet8` or `--runtime dotnet9` for managed runtimes.

NativeAOT binary-per-feature packaging is available with:

```bash
slicefx package aws-lambda --mode function-per-feature --rid linux-x64 --output artifacts/aws-lambda
```

Use `--skip-publish` to emit per-feature wrapper projects and validate eligibility without running `dotnet publish`. This is useful for eligibility checks and CI environments that do not have the NativeAOT toolchain installed:

```bash
slicefx package aws-lambda --mode function-per-feature --skip-publish --output artifacts/aws-lambda
```

This generates one temporary entry project per eligible Lambda function-per-feature route under the package `obj` directory, publishes each route to a distinct artifact directory, and writes one package manifest artifact per feature. It may be slow because it runs one NativeAOT publish per eligible feature. Generated wrappers use size-oriented NativeAOT settings, route-local JSON source-generation metadata, supported DataAnnotations validation without runtime reflection, and NativeAOT mstat/map output.

The command writes `slicefx-lambda-package-report.json` containing per-artifact size, top files, binlog path, structured warning details, mstat/map paths, and closure inspection results. Without `--warning-baseline`, a real publish must produce zero warnings. Pass `--warning-baseline <path>` to allow known warnings; stale baseline entries fail. Closure inspection fails if an artifact roots sibling feature entrypoints, sibling feature-owned DTOs, sibling validators, generated app-wide registration surfaces, the ASP.NET hosted bootstrap path, hosted Lambda adapter types, or unrelated SliceFx satellite types.

PR CI gates the NativeAOT fixture package on `linux-x64`. The `linux-arm64` package gate is scheduled/manual because runner availability varies; it uses a Linux ARM64 runner when one is available. Each artifact runs as an independent Lambda process and owns independent DI singleton state. WASI per-feature packaging is not implemented.

The `slicefx package` surface currently exists for AWS Lambda function-per-feature artifacts only. WASI deployment remains a `dotnet publish -r wasi-wasm` flow that produces one `wasi:http` component containing the generated route table.
