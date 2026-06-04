# AWS Lambda

[English](../lambda.md) | [日本語 docs index](README.md)

> この日本語版は参考訳です。仕様判断は英語版を正本とします。

SliceFx には2つの Lambda path があります。

- `SliceFx.Lambda` — full ASP.NET Core app を1つの Lambda function として host します。
- `SliceFx.Lambda.FunctionPerFeature` — eligible feature ごとに NativeAOT HTTP API v2 handler を emit し、`slicefx` CLI が独立 binary として package します。

function-per-feature を Lambda deployment model として扱う戦略的背景は [product-direction.md](../product-direction.md) (English) を参照してください。

## Choosing a mode: Hosted vs Function-per-feature

| | Hosted Lambda | Function-per-feature Lambda |
| --- | --- | --- |
| Deploy artifact | 1つの binary | eligible feature ごとに1つの NativeAOT binary |
| Cold start | 高め (full ASP.NET host) | 低め (minimal custom-runtime binary) |
| DI scope | app-wide shared container | feature ごとの independent container |
| Singleton state | 全 feature で shared | feature 間で分離 |
| Endpoint filters (`[Filter<T>]`) | supported | not supported (excluded, SLICE031) |
| AOT | optional | required |
| 向いている場合 | single deployment unit、shared state、Minimal API からの素早い移行 | per-feature scale、cold-start sensitivity、blast-radius isolation |

## Hosted Lambda

`SliceFx.Lambda` は `Amazon.Lambda.AspNetCoreServer.Hosting` の薄い adapter です。

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

`UseSliceLambda()` は `AddAWSLambdaHosting()` に委譲します。local では同じ binary が Kestrel で動き、Lambda では hosting package が `LAMBDA_TASK_ROOT` によって runtime environment を検出します。

target runtime identifier (`linux-x64`、`linux-arm64` など) に合わせて Lambda .NET tooling (`dotnet lambda package`) で deploy します。working example は `samples/SliceFx.LambdaSample/` を参照してください。

default event source は `LambdaEventSource.HttpApi` (API Gateway HTTP API v2) です。`LambdaEventSource.RestApi` または `LambdaEventSource.ApplicationLoadBalancer` を渡して override できます。

## Function-per-feature Lambda

`SliceFx.Lambda.FunctionPerFeature` は eligible feature 向けの generated HTTP API v2 handler を emit します。各 feature は独立した NativeAOT Lambda custom-runtime artifact になります。

### Pipeline

```text
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
    artifacts/aws-lambda/<feature>/bootstrap.zip
    artifacts/aws-lambda/slicefx-lambda-package.json
    artifacts/aws-lambda/slicefx-lambda-package-report.json
```

### Try it

DataAnnotations + `ISliceValidator<T>` validation と per-feature startup を含む working example は [samples/SliceFx.LambdaFunctionPerFeatureSample](../../samples/SliceFx.LambdaFunctionPerFeatureSample/README.md) を参照してください。

### Programming model

assembly level で opt in します。通常は `LambdaSetup.cs` のような専用 file に置きます。

```csharp
[assembly: LambdaFunctionPerFeature]
```

DI setup が必要な feature には feature-scoped startup type を annotation します。

```csharp
[Feature("POST /orders")]
[LambdaFunctionStartup(typeof(OrderFeatureStartup))]
public static class CreateOrder { ... }
```

startup type は `ILambdaFunctionPerFeatureStartup` を実装し、public parameterless constructor を持つ必要があります。

```csharp
public sealed class OrderFeatureStartup : ILambdaFunctionPerFeatureStartup
{
    public void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IOrderStore, InMemoryOrderStore>();
}
```

source generator は startup type を発見し、artifact ごとの cold-start で `ConfigureServices` を1回呼びます。

#### Supported handler inputs

- route parameters (`{id:int}`, `{name}`)
- query parameters (`[FromQuery(Name = "...")]`)
- header parameters (`[FromHeader(Name = "...")]`)
- JSON body contract types, nested `Request` record, non-nested shared DTO
- explicit `[FromBody]` JSON body
- DI services — interface / abstract type は DI と推論されます。**concrete service type には `[FromServices]` が必要**です。generator は compile-time heuristic を使い DI container を probe できないため、annotation なしの concrete type は body candidate とみなされ SLICE033 で除外されます。詳しくは [parameter-binding.md](guides/parameter-binding.md)。
- `CancellationToken`

missing nullable parameter は `null` に bind され、missing non-nullable parameter は `400 Bad Request` になります。present empty string は `""` に bind されます。

#### Supported return shapes

- POCO records / classes
- `Task<T>`
- `ValueTask<T>`
- `APIGatewayHttpApiV2ProxyResponse`
- `Task<APIGatewayHttpApiV2ProxyResponse>`
- `ValueTask<APIGatewayHttpApiV2ProxyResponse>`

typed return value が `null` の場合、generated handler は `200 application/json` と JSON `null` body を返します。`null` を 404、204、non-JSON response として扱いたい場合は `APIGatewayHttpApiV2ProxyResponse` を直接返してください。

#### JSON body and response serialization

CLI は wrapper project ごとに route-local `JsonSerializerContext` を生成し、API Gateway envelope type とその route の body/response root だけを含めます。これにより sibling feature DTO を全 artifact に root せず、JSON metadata を AOT-safe に保てます。Lambda function-per-feature path では user-authored `SliceJsonContext` は不要です。

### Per-feature isolation

各 function-per-feature artifact は独立した Lambda process で、以下を持ちます。

- **Independent DI container** — `ILambdaFunctionPerFeatureStartup.ConfigureServices` は process ごとに1回呼ばれ、singleton はその feature lifetime に scope されます。
- **No shared singleton state** — ある startup type に登録された singleton は別 feature process から見えません。
- **Closure inspection** — `slicefx package` は artifact binary が sibling feature entrypoint、sibling-owned DTO、validator、app-wide registration surface、ASP.NET hosted bootstrap、hosted Lambda adapter type、unrelated SliceFx satellite type を root していないことを検証します。

この isolation により blast radius は invoked feature に限定されます。詳しくは [design-decisions.md](design-decisions.md#なぜ-function-per-feature-artifact-は-feature-ごとに-process-と-di-container-を持つのか) を参照してください。

### Diagnostics reference

Lambda function-per-feature の diagnostic は [source generator diagnostics reference](source-generator.md#diagnostics) の `SLICE030`-`SLICE039` range を参照してください。

## CLI integration

hosted Lambda 用 SAM template:

```bash
slicefx manifest aws-lambda --output template.yaml
```

function-per-feature SAM template:

```bash
slicefx manifest aws-lambda --mode function-per-feature --output template.yaml
```

function-per-feature NativeAOT artifact package:

```bash
slicefx package aws-lambda --mode function-per-feature --rid linux-x64 --output artifacts/aws-lambda
```

`dotnet publish` を実行せず wrapper project と eligibility だけ検証する場合:

```bash
slicefx package aws-lambda --mode function-per-feature --skip-publish --output artifacts/aws-lambda
```

available flag の詳細は [cli.md](cli.md#aws-lambda-artifacts) を参照してください。

## NativeAOT packaging notes

function-per-feature path は AOT-compatible に保たれます。JSON serialization は wrapper project ごとの source-generated `JsonSerializerContext` metadata を使い、supported DataAnnotations rule は runtime reflection なしで生成され、unsupported shape は packaging 前に除外されます。

`slicefx package` は `slicefx-lambda-package-report.json` を書きます。per-artifact size、top files、binlog path、structured warning summary、mstat/map path、closure inspection result を含みます。`--warning-baseline` なしでは publish warning が1つでもあると package は失敗します。baseline 指定時も unbaselined warning と stale baseline entry は失敗します。

PR CI は `linux-x64` package gate を実行します。`linux-arm64` NativeAOT gate は scheduled/manual workflow で実行されます。

WASI per-feature packaging は実装されていません。
