# SliceFx.LambdaFunctionPerFeatureSample

Demonstrates `SliceFx.Lambda.FunctionPerFeature`: one NativeAOT Lambda custom-runtime artifact per eligible feature, packaged and deployed independently.

This sample runs locally on Kestrel for development, and can be packaged into per-feature NativeAOT binaries by the `slicefx` CLI.

## What this sample shows

| File | Demonstrates |
|---|---|
| `LambdaSetup.cs` | Assembly-level opt-in (`[assembly: LambdaFunctionPerFeature]`) |
| `Features/Orders/CreateOrder.cs` | POST route, `Request` body binding, `[Required]`/`[StringLength]`/`[Range]` DataAnnotations, `ISliceValidator<T>` custom validation, DI, `[LambdaFunctionStartup]` |
| `Features/Orders/GetOrder.cs` | GET route, `{id:int}` route token, `[FromQuery]`, `[FromHeader]`, `CancellationToken` |
| `Features/Orders/OrderFeatureStartup.cs` | `ILambdaFunctionPerFeatureStartup` — per-feature isolated DI container |
| `Services/OrderStore.cs` | `IOrderStore` abstraction with in-memory implementation |

Features return plain POCO records. Returning `IResult` or `Task<IResult>` would make a feature ineligible for the function-per-feature path (SLICE030).

## Run locally

```bash
dotnet run --project samples/SliceFx.LambdaFunctionPerFeatureSample
```

The sample listens on `http://localhost:5000` by default.

```bash
# Create an order
curl -s -X POST http://localhost:5000/orders \
  -H 'content-type: application/json' \
  -d '{"sku":"SKU-001","quantity":3}'

# Get an order (route token + query + header)
curl -s "http://localhost:5000/orders/42?details=true" \
  -H 'x-trace-id: abc-123'

# Validation failure from ISliceValidator<T>
curl -s -X POST http://localhost:5000/orders \
  -H 'content-type: application/json' \
  -d '{"sku":"blocked-sku","quantity":1}'
# → 400 Problem Details: SKU is blocked.

# Validation failure from DataAnnotations
curl -s -X POST http://localhost:5000/orders \
  -H 'content-type: application/json' \
  -d '{"sku":"x","quantity":0}'
# → 400 Problem Details: StringLength / Range violations
```

## Package for Lambda (function-per-feature)

Install the `slicefx` CLI if not already done:

```bash
dotnet tool restore
```

Generate a SAM template:

```bash
slicefx manifest aws-lambda \
  --mode function-per-feature \
  --project samples/SliceFx.LambdaFunctionPerFeatureSample/SliceFx.LambdaFunctionPerFeatureSample.csproj \
  --output template.yaml
```

Package NativeAOT artifacts (requires Linux x64 host or Docker linux/amd64):

```bash
slicefx package aws-lambda \
  --mode function-per-feature \
  --project samples/SliceFx.LambdaFunctionPerFeatureSample/SliceFx.LambdaFunctionPerFeatureSample.csproj \
  --rid linux-x64 \
  --output artifacts/aws-lambda
```

Each eligible feature produces a `bootstrap.zip` in `artifacts/aws-lambda/`. The command also writes `slicefx-lambda-package.json` (artifact index) and `slicefx-lambda-package-report.json` (size, closure inspection, trimming diagnostics).

Use `--skip-publish` to emit wrapper projects and validate eligibility without running `dotnet publish`:

```bash
slicefx package aws-lambda \
  --mode function-per-feature \
  --project samples/SliceFx.LambdaFunctionPerFeatureSample/SliceFx.LambdaFunctionPerFeatureSample.csproj \
  --skip-publish \
  --output artifacts/aws-lambda
```

## Further reading

See [docs/lambda.md](../../docs/lambda.md) for the full API contract, binding rules, diagnostics reference, and comparison with hosted Lambda.
