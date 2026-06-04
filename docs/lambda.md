# AWS Lambda

[日本語](ja/lambda.md)

SliceFx has two Lambda paths:

- `SliceFx.Lambda` — hosts the full ASP.NET Core app in a single Lambda function.
- `SliceFx.Lambda.FunctionPerFeature` — emits one NativeAOT HTTP API v2 handler per eligible feature; the `slicefx` CLI packages them as independent binaries.

See [product-direction.md](product-direction.md) for the strategic context behind function-per-feature as a Lambda deployment model.

## Choosing a mode: Hosted vs Function-per-feature

| | Hosted Lambda | Function-per-feature Lambda |
| --- | --- | --- |
| Deploy artifact | One binary | One NativeAOT binary per eligible feature |
| Cold start | Higher (full ASP.NET host) | Lower (minimal custom-runtime binary) |
| DI scope | Shared app-wide container | Independent container per feature |
| Singleton state | Shared across all features | Isolated — no state bleeds between features |
| Endpoint filters (`[Filter<T>]`) | Supported | Not supported (excluded, SLICE031) |
| AOT | Optional | Required (per-feature wrapper) |
| When to reach for it | Single deployment unit, shared state, quick migration from Minimal API | Per-feature scale, cold-start sensitivity, blast-radius isolation |

## Hosted Lambda

`SliceFx.Lambda` is a thin adapter over `Amazon.Lambda.AspNetCoreServer.Hosting`.

```csharp
using SliceFx;
using SliceFx.Lambda;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.UseSliceLambda();

var app = builder.Build();

app.MapSlices();

await app.RunOnLambdaAsync();
```

`UseSliceLambda()` delegates to `AddAWSLambdaHosting()`. Locally, the same binary runs on Kestrel; in Lambda, the hosting package detects the runtime environment via `LAMBDA_TASK_ROOT`.

Deploy with the Lambda .NET tooling (`dotnet lambda package`) for the target runtime identifier, such as `linux-x64` or `linux-arm64`. See [samples/SliceFx.LambdaSample/](../samples/SliceFx.LambdaSample/) for a working example.

The default event source is `LambdaEventSource.HttpApi` (API Gateway HTTP API v2). Pass `LambdaEventSource.RestApi` or `LambdaEventSource.ApplicationLoadBalancer` to override.

## Function-per-feature Lambda

`SliceFx.Lambda.FunctionPerFeature` emits generated HTTP API v2 handlers for eligible features. Each feature becomes an independent NativeAOT Lambda custom-runtime artifact.

### Pipeline

```
[*.cs features with [assembly: LambdaFunctionPerFeature]]
    │
    ▼ SourceGenerator
    {Asm}.SliceLambdaFunctionPerFeatureHandlers.g.cs   ← generated handlers + route metadata
    │
    ▼ slicefx manifest aws-lambda --mode function-per-feature
    template.yaml                                       ← SAM template, one Function per feature
    │
    ▼ slicefx package aws-lambda --mode function-per-feature --rid linux-x64
    obj/slicefx/aws-lambda/per-feature/<route>/        ← per-feature wrapper project
        bootstrap.csproj  (PublishAot=true, route-local JsonSerializerContext)
    │
    ▼ dotnet publish ×N (one per eligible feature)
    artifacts/aws-lambda/<feature>/bootstrap.zip            ← Lambda custom-runtime artifact
    artifacts/aws-lambda/slicefx-lambda-package.json        ← artifact index
    artifacts/aws-lambda/slicefx-lambda-package-report.json ← sizes, closure inspection, warnings
```

### Try it

See [samples/SliceFx.LambdaFunctionPerFeatureSample/](../samples/SliceFx.LambdaFunctionPerFeatureSample/README.md) for a complete working example with two features, DataAnnotations + `ISliceValidator<T>` validation, and per-feature startup.

### Programming model

Opt in at the assembly level (typically in a dedicated file such as `LambdaSetup.cs`):

```csharp
[assembly: LambdaFunctionPerFeature]
```

For features that need DI setup, annotate each with a feature-scoped startup type:

```csharp
[Feature("POST /orders")]
[LambdaFunctionStartup(typeof(OrderFeatureStartup))]
public static class CreateOrder { ... }
```

The startup type must implement `ILambdaFunctionPerFeatureStartup` and have a public parameterless constructor:

```csharp
public sealed class OrderFeatureStartup : ILambdaFunctionPerFeatureStartup
{
    public void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IOrderStore, InMemoryOrderStore>();
}
```

The source generator discovers startup types and calls `ConfigureServices` once at cold-start for each artifact.

#### Supported handler inputs

- Route parameters (`{id:int}`, `{name}`)
- Query parameters (`[FromQuery(Name = "...")]`)
- Header parameters (`[FromHeader(Name = "...")]`)
- JSON body contract types, including nested `Request` records and non-nested shared DTOs
- Explicit `[FromBody]` JSON bodies
- DI services — interface and abstract types are inferred from DI automatically; **concrete service types require `[FromServices]`** because the Lambda function-per-feature generator uses a compile-time heuristic and cannot probe the DI container (an un-annotated concrete type becomes a body candidate and the feature is excluded with SLICE033). See [parameter-binding.md](guides/parameter-binding.md).
- `CancellationToken`

Missing nullable parameters bind `null`; missing non-nullable parameters return `400 Bad Request`. Present empty strings bind as `""`.

#### Supported return shapes

- POCO records / classes
- `Task<T>`
- `ValueTask<T>`
- `APIGatewayHttpApiV2ProxyResponse`
- `Task<APIGatewayHttpApiV2ProxyResponse>`
- `ValueTask<APIGatewayHttpApiV2ProxyResponse>`

If a typed return value is `null`, the generated handler returns `200 application/json` with a JSON `null` body. Use `APIGatewayHttpApiV2ProxyResponse` when `null` should instead mean 404, 204, or another non-JSON response.

#### JSON body and response serialization

The CLI generates one route-local `JsonSerializerContext` per wrapper project containing only the API Gateway envelope types and that route's body/response roots. This keeps JSON metadata AOT-safe without globally rooting sibling feature DTOs in every artifact. No user-authored `SliceJsonContext` is required for the Lambda function-per-feature path.

### Per-feature isolation

Each function-per-feature artifact is an independent Lambda process with:

- **Independent DI container** — `ILambdaFunctionPerFeatureStartup.ConfigureServices` is called once per process; singletons are scoped to that feature's lifetime.
- **No shared singleton state** — a singleton registered in one startup type is never visible to another feature's process.
- **Closure inspection** — the `slicefx package` command verifies that each artifact's binary does not root sibling feature entrypoints, sibling feature-owned DTOs, sibling validators, app-wide registration surfaces, the ASP.NET hosted bootstrap, hosted Lambda adapter types, or unrelated SliceFx satellite types. A violation fails the package.

This isolation limits the blast radius to the feature being invoked. See [docs/design-decisions.md](design-decisions.md#why-does-each-function-per-feature-artifact-get-its-own-process-and-di-container) for the full rationale.

### Diagnostics reference

See the Lambda function-per-feature entries in the central [source generator diagnostics reference](source-generator.md#diagnostics). The relevant range is `SLICE030`-`SLICE039`.

## CLI integration

Generate a SAM template for hosted Lambda:

```bash
slicefx manifest aws-lambda --output template.yaml
```

Generate a function-per-feature SAM template:

```bash
slicefx manifest aws-lambda --mode function-per-feature --output template.yaml
```

Package function-per-feature NativeAOT artifacts:

```bash
slicefx package aws-lambda --mode function-per-feature --rid linux-x64 --output artifacts/aws-lambda
```

Use `--skip-publish` to emit wrapper projects and validate eligibility without running `dotnet publish`:

```bash
slicefx package aws-lambda --mode function-per-feature --skip-publish --output artifacts/aws-lambda
```

For the full list of available flags (`--artifact-layout`, `--configuration`, `--runtime`, `--memory`, `--timeout`, `--warning-baseline`), see [docs/cli.md](cli.md#aws-lambda-artifacts).

## NativeAOT packaging notes

The function-per-feature path stays AOT-compatible by design: JSON serialization uses source-generated `JsonSerializerContext` metadata scoped to each wrapper project, supported DataAnnotations rules are generated without runtime reflection, and unsupported shapes are excluded before packaging.

The `slicefx package` command writes `slicefx-lambda-package-report.json` with per-artifact size, top files, binlog path, structured warning summary, mstat/map paths, and closure inspection results. Without `--warning-baseline`, any publish warning fails the package; with `--warning-baseline <path>`, unbaselined warnings and stale baseline entries both fail.

CI gates the PR package on `linux-x64`. The `linux-arm64` NativeAOT gate runs from the scheduled/manual `Lambda NativeAOT arm64` workflow.

WASI per-feature packaging is not implemented.
