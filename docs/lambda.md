# AWS Lambda

Slice has two Lambda paths:

- `Slice.Lambda` hosts the ASP.NET Core app in Lambda.
- `Slice.Lambda.PerFunction` emits generated HTTP API v2 handlers for eligible features.

Use hosted Lambda by default. Use per-feature Lambda when you explicitly want route-level Lambda functions and can accept the MVP constraints.

## Hosted Lambda

`Slice.Lambda` is a thin adapter over `Amazon.Lambda.AspNetCoreServer.Hosting`.

```csharp
using Slice.Lambda;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.UseSliceLambda();

var app = builder.Build();

app.MapSlices();

await app.RunOnLambdaAsync();
```

`UseSliceLambda()` delegates to `AddAWSLambdaHosting()`. Locally, the same binary runs on Kestrel; in Lambda, the hosting package detects the runtime environment.

Deploy the sample with the Lambda .NET tooling (`dotnet lambda package`) for the target runtime identifier, such as `linux-x64` or `linux-arm64`.

## Per-feature Lambda

`Slice.Lambda.PerFunction` is an HTTP API v2 MVP. Opt in at the assembly level:

```csharp
[assembly: LambdaPerFunction]
```

For DI setup, pass a startup type:

```csharp
[assembly: LambdaPerFunction(typeof(MyStartup))]
```

JSON body and response routes must provide a source-generated `JsonSerializerContext` marked with `[SliceJsonContext(SliceJsonTarget.LambdaPerFeature)]`. The context can have any name or namespace, but it must include `[JsonSerializable]` roots for the request and response types used by eligible per-feature handlers.

Supported handler inputs:

- Route parameters
- Query parameters
- Nested JSON `Request` bodies
- DI services
- `CancellationToken`

Supported return shapes:

- POCOs
- `Task<T>`
- `ValueTask<T>`

Unsupported routes are excluded with generator diagnostics and CLI reasons. Common exclusions include:

- `IResult` return types
- Endpoint filters
- Reflection-only validation
- Unsupported route parameter types
- JSON body or response routes without a marked Lambda per-feature `SliceJsonContext`

The generator reports `SLICE012`-`SLICE017` for per-feature Lambda eligibility issues, plus `SLICE018`/`SLICE019` for invalid explicit JSON context overrides.

## CLI integration

Generate a hosted SAM template:

```bash
slice manifest aws-lambda --output template.yaml
```

Generate a per-feature SAM template:

```bash
slice manifest aws-lambda --mode per-feature --output template.yaml
```

Package per-feature artifacts:

```bash
slice package aws-lambda --mode per-feature --output artifacts/aws-lambda
```

The per-feature MVP may point multiple functions at the same publish artifact and select the generated method through the Lambda handler. True NativeAOT binary-per-feature packaging remains future work.
