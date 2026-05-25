# AWS Lambda

SliceFx has two Lambda paths:

- `SliceFx.Lambda` hosts the ASP.NET Core app in Lambda.
- `SliceFx.Lambda.FunctionPerFeature` emits generated HTTP API v2 handlers for eligible features.

Use hosted Lambda by default. Use function-per-feature Lambda when you explicitly want route-level Lambda function resources and can accept the shared-artifact MVP constraints.

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

`SliceFx.Lambda.FunctionPerFeature` is an HTTP API v2 MVP. It emits one Lambda function resource per eligible feature, but the current artifact layout is shared: multiple functions point at the same publish output and select the generated method through `Handler`. This does not provide per-function binary-size or cold-start isolation. Opt in at the assembly level:

```csharp
[assembly: LambdaFunctionPerFeature]
```

For DI setup, pass a startup type:

```csharp
[assembly: LambdaFunctionPerFeature(typeof(MyStartup))]
```

JSON body and response routes must provide a source-generated `JsonSerializerContext` marked with `[SliceJsonContext(SliceJsonTarget.LambdaFunctionPerFeature)]`. The context can have any name or namespace, but it must include `[JsonSerializable]` roots for the request and response types used by eligible function-per-feature handlers.

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
- JSON body or response routes without a marked Lambda function-per-feature `SliceJsonContext`

The generator reports `SLICE012`-`SLICE017` for function-per-feature Lambda eligibility issues, plus `SLICE018`/`SLICE019` for invalid explicit JSON context overrides.

## CLI integration

Generate a hosted SAM template:

```bash
slicefx manifest aws-lambda --output template.yaml
```

Generate a function-per-feature SAM template with the current shared artifact layout:

```bash
slicefx manifest aws-lambda --mode function-per-feature --artifact-layout shared --output template.yaml
```

Package function-per-feature artifacts with the current shared artifact layout:

```bash
slicefx package aws-lambda --mode function-per-feature --artifact-layout shared --output artifacts/aws-lambda
```

`--artifact-layout shared` is the only supported layout today. The generated route and package metadata already model the shared publish output as a named Lambda artifact so future per-feature artifacts can use the same shape without changing the feature model. True NativeAOT binary-per-feature packaging is still reserved for a future `--artifact-layout per-feature` implementation.

## NativeAOT binary-per-feature groundwork

The generated function-per-feature handler path is designed to stay AOT-compatible: JSON body and response handling uses source-generated `JsonSerializerContext` metadata, generated validation avoids reflection-only DataAnnotations rules, and unsupported ASP.NET-specific shapes are excluded before packaging.

A minimal generated handler has been verified to publish and run as a local NativeAOT executable, which proves the generated handler and `SliceFx.Lambda.FunctionPerFeature` binding/response path can survive NativeAOT in principle. That is not the same as true Lambda binary-per-feature packaging yet: a production `--artifact-layout per-feature` still needs generated per-feature entry projects, a Lambda custom-runtime bootstrap, target runtime/RID handling such as `linux-x64` or `linux-arm64`, package caching, and explicit documentation that each binary owns an independent DI container and process.
