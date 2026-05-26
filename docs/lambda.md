# AWS Lambda

SliceFx has two Lambda paths:

- `SliceFx.Lambda` hosts the ASP.NET Core app in Lambda.
- `SliceFx.Lambda.FunctionPerFeature` emits generated HTTP API v2 handlers for eligible features.

Use hosted Lambda when you want one ASP.NET-hosted Lambda artifact. Use function-per-feature Lambda when you explicitly want one NativeAOT custom-runtime artifact per eligible feature.

## Hosted Lambda

`SliceFx.Lambda` is a thin adapter over `Amazon.Lambda.AspNetCoreServer.Hosting`.

```csharp
using SliceFx.Lambda;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.UseSliceLambda();

var app = builder.Build();

app.MapSlices();

await app.RunOnLambdaAsync();
```

`UseSliceLambda()` delegates to `AddAWSLambdaHosting()`. Locally, the same binary runs on Kestrel; in Lambda, the hosting package detects the runtime environment.

Deploy the sample with the Lambda .NET tooling (`dotnet lambda package`) for the target runtime identifier, such as `linux-x64` or `linux-arm64`.

## Function-per-feature Lambda

`SliceFx.Lambda.FunctionPerFeature` emits HTTP API v2 handlers for eligible features and the CLI packages them as one NativeAOT Lambda custom-runtime artifact per feature. Opt in at the assembly level:

```csharp
[assembly: LambdaFunctionPerFeature]
```

For DI setup, annotate each feature with a feature-scoped startup type:

```csharp
[LambdaFunctionStartup(typeof(MyFeatureStartup))]
public static class CreateOrder
{
}
```

JSON body and response routes do not need a user-authored Lambda `SliceJsonContext`. During packaging, the CLI generates one route-local `JsonSerializerContext` inside each wrapper project with only the API Gateway envelope types and that route's body/response roots. This keeps JSON metadata AOT-safe without globally rooting sibling feature DTOs in every artifact.

Supported handler inputs:

- Route parameters
- Query parameters
- Header parameters
- Nested JSON `Request` bodies
- Explicit `[FromBody]` JSON bodies
- DI services
- `CancellationToken`

Generated function-per-feature handlers honor common Minimal API binding attributes:
`[FromRoute(Name = "...")]`, `[FromQuery(Name = "...")]`,
`[FromHeader(Name = "...")]`, `[FromBody]`, and `[FromServices]`.
Route, query, and header values use the same required/optional contract as
the WASI path: missing nullable parameters bind `null`, missing non-nullable
parameters return `400 Bad Request`, and present-but-invalid values always
return `400 Bad Request`. Present empty strings bind as `""`; present empty
nullable scalars bind `null`.

Supported return shapes:

- POCOs
- `Task<T>`
- `ValueTask<T>`
- `APIGatewayHttpApiV2ProxyResponse`
- `Task<APIGatewayHttpApiV2ProxyResponse>`
- `ValueTask<APIGatewayHttpApiV2ProxyResponse>`

If a typed return value is `null`, the generated handler returns
`200 application/json` with a JSON `null` body. Return
`APIGatewayHttpApiV2ProxyResponse` when `null` should instead mean 404, 204,
or another non-JSON response.

Unsupported routes are excluded with generator diagnostics and CLI reasons. Common exclusions include:

- `IResult` return types
- Endpoint filters
- Reflection-only validation
- Unsupported route parameter types
- JSON body or response roots that cannot be emitted into the route-local wrapper context

The generator reports `SLICE012`-`SLICE017` for function-per-feature Lambda eligibility issues. The WASI path still uses explicit `[SliceJsonContext(SliceJsonTarget.Wasi)]` metadata.

## CLI integration

Generate a hosted SAM template:

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

## NativeAOT binary-per-feature packaging

The generated function-per-feature handler path is designed to stay AOT-compatible: JSON body and response handling uses source-generated `JsonSerializerContext` metadata, supported DataAnnotations rules are generated without runtime reflection, and unsupported ASP.NET-specific or reflection-bound shapes are excluded before packaging.

```bash
slicefx package aws-lambda --mode function-per-feature --rid linux-x64 --output artifacts/aws-lambda
```

The CLI generates one temporary entry project per eligible function-per-feature route under the package `obj` directory, publishes each entry project as an independent NativeAOT Lambda custom-runtime artifact, and writes `slicefx-lambda-package.json` with one artifact per feature. Unsupported routes stay excluded with Lambda function-per-feature eligibility reasons.

Expect this mode to be slow because it runs one NativeAOT publish per eligible feature. The generated wrapper projects use full trimming, size-focused ILC optimization, NativeAOT map/mstat output, and route-local JSON metadata. The package command writes `slicefx-lambda-package-report.json` with a schema version, per-artifact size, top files, binlog path, structured warning summary, mstat/map paths, and closure inspection results. Without `--warning-baseline`, any publish warning fails the package; with `--warning-baseline <path>`, unbaselined current warnings and stale baseline entries both fail. Closure inspection fails if a per-feature artifact roots sibling feature entrypoints, sibling feature-owned DTOs, sibling validators, generated app-wide registration surfaces, ASP.NET hosted bootstrap, hosted Lambda adapter types, or unrelated SliceFx satellite types. Each artifact is a separate Lambda process with its own dependency injection container and independent singleton state. WASI per-feature packaging is not implemented.

CI keeps the PR package gate on `linux-x64`. The `linux-arm64` NativeAOT package gate runs from the scheduled/manual `Lambda NativeAOT arm64` workflow and expects an available Linux ARM64 runner, such as a self-hosted runner.
