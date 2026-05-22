# Slice CLI

`Slice.Cli` is the local `slice` command for scaffolding, route inspection, client generation, and deployment artifacts.

## Commands

```bash
slice new feature CreateOrder --method POST --route /orders
slice new feature GetProductDetail --method GET
slice new filter RequireApiKeyFilter
slice new wasi-cloudflare

slice routes
slice routes --format json

slice client csharp --output SliceApiClient.g.cs

slice manifest aws-lambda --output template.yaml
slice manifest aws-lambda --mode per-feature --output template.yaml
slice package aws-lambda --mode per-feature --output artifacts/aws-lambda
```

Pass `--project` when running outside the project directory. Use `--force` for commands that write files and should overwrite existing output.

## Scaffolding

`slice new feature` detects the target project, reads `<RootNamespace>`, infers the feature group from common verb prefixes, and writes to `Features/<Group>/<FeatureName>.cs`.

Examples:

| Feature name | Default group |
| --- | --- |
| `CreateUser` | `Users` |
| `ListOrders` | `Orders` |
| `GetProductDetail` | `Products` |

Generated feature templates return a nested `Response` record by default. `POST`, `PUT`, and `PATCH` templates also include an empty `Request`.

`slice new filter` scaffolds an `IEndpointFilter`.

`slice new wasi-cloudflare` scaffolds Cloudflare Workers host files for a `Slice.Wasi` component into `dist/`: `shim.mjs`, `package.json`, Wrangler config, socket stubs, and module-map generation. App-specific pieces such as `IncomingHandlerImpl.cs` and `WasiJsonContext.cs` remain in the app because they depend on WIT-generated types and user DTO metadata.

## Route inspection

`slice routes` reads source-generated route metadata from the built project output when available, including directly referenced Slice feature assemblies copied beside the app. If the project has not been built yet, it falls back to scanning `Features/**/*.cs`.

The command reports each route's portability:

| Status | Meaning |
| --- | --- |
| `portable` | The handler shape avoids ASP.NET-specific return types and can be considered for WASI-style dispatch. |
| `partial` | The route shape is portable, but some attached behavior such as non-validator endpoint filters is ASP.NET-only today. |
| `aspnet-only` | The route intentionally depends on ASP.NET concepts such as `IResult`. |

`--format json` exports the same route metadata for tooling.

## Typed C# client

`slice client csharp` generates a typed `HttpClient` wrapper for portable and partial routes. This is useful for Blazor and other .NET clients that should not hand-maintain endpoint strings and DTO wiring.

## AWS Lambda artifacts

`slice manifest aws-lambda` reads the source-generated route manifest and writes an AWS SAM `template.yaml`.

By default (`--mode hosted`), it emits one `AWS::Serverless::Function` for the ASP.NET-hosted Slice app and one API Gateway `HttpApi` event per `[Feature]`. All features are included because `Slice.Lambda` runs through ASP.NET Core hosting.

`--mode per-feature` emits one `AWS::Serverless::Function` per eligible generated `Slice.Lambda.PerFunction` handler and excludes unsupported routes with reasons. ASP.NET route constraints such as `{id:guid}` are converted to API Gateway syntax such as `{id}`.

The default runtime is `provided.al2023`. Use `--runtime dotnet8` or `--runtime dotnet9` for managed runtimes.

`slice package aws-lambda --mode per-feature` creates a publish output and `slice-lambda-package.json` describing generated handlers. The manifest records the publish directory as the artifact-relative `publish` path. The current MVP may point multiple functions at the same publish artifact; separate NativeAOT binaries per feature remain a future optimization.
