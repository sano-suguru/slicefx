# SliceFx CLI

[English](../cli.md) | [日本語 docs index](README.md)

> この日本語版は参考訳です。仕様判断は英語版を正本とします。

`SliceFx.Cli` は local `slicefx` command です。scaffolding、route inspection、client generation、deployment artifact generation を提供します。

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

project directory の外から実行する場合は `--project` を渡します。file を書き込む command で既存 output を上書きしたい場合は `--force` を使います。

## Scaffolding

`slicefx new feature` は target project を検出し、`<RootNamespace>` を読み、common verb prefix から feature group を推論して `Features/<Group>/<FeatureName>.cs` に書き込みます。

| Feature name | Default group |
| --- | --- |
| `CreateUser` | `Users` |
| `ListOrders` | `Orders` |
| `GetProductDetail` | `Products` |

generated feature template は既定で nested `Response` record を返します。`POST`、`PUT`、`PATCH` template には空の `Request` も含まれます。

`slicefx new filter` は `IEndpointFilter` を scaffold します。

`slicefx new wasi-cloudflare` は `SliceFx.Wasi` component 向け Cloudflare Workers host file を `dist/` に scaffold します。`shim.mjs`、`package.json`、Wrangler config、socket stubs、module-map generation などです。`IncomingHandlerImpl.cs` や `[SliceJsonContext(SliceJsonTarget.Wasi)]` JSON context など app-specific な部分は、WIT-generated type や user DTO metadata に依存するため app 側に残します。

scaffold は Cloudflare JS tool version と Node.js 22+ を pin します。lockfile は emit しないため、初回は `npm install` を実行して生成された `package-lock.json` を review / commit し、その後は `npm ci` を使います。upstream WASI build/transpile toolchain は preview/unstable です。

この command は single-component WASI deployment glue を scaffold します。per-feature WASM artifact は作らず、現在 `slicefx package wasi` command もありません。

## Route inspection

`slicefx routes` は build 済み project output から source-generated route metadata を読みます。referenced Slice feature assembly については、host が generated metadata で明示的に aggregate した assembly (`SliceFxReferencedAssemblies` または `SliceFxAggregateReferences=true`) だけを含め、stderr notice に assembly 名を出します。project がまだ build されていない場合は local `Features/**/*.cs` を scan する fallback を使います。

portability は以下のように表示されます。

| Status | Meaning |
| --- | --- |
| `portable` | handler shape が ASP.NET-specific return type を避けており、WASI-style dispatch の候補になります。 |
| `partial` | route shape は portable ですが、endpoint filter など attached behavior の一部が現在 ASP.NET-only です。 |
| `aspnet-only` | route が `IResult` など ASP.NET concept に意図的に依存しています。 |

table output には route の source assembly を示す `SOURCE` column が含まれます。`--format json` は同じ route metadata を JSON として export し、`sourceAssemblyName` も含みます。

## Typed C# client

`slicefx client csharp` は portable / partial route 向けに typed `HttpClient` wrapper を生成します。Blazor や .NET client が endpoint string と DTO wiring を手で保守しないために使えます。

C# client は Slice handler signature の C# contract type を再利用し、DTO copy は emit しません。client project は request/response type を含む assembly を参照する必要があります。nested feature DTO (`CreateUser.Request`, `CreateUser.Response`) を使う場合は feature assembly が見えている必要があります。Blazor や shared .NET client が server feature assembly を参照したくない場合は、request/response record を shared contracts project に置いて handler signature で使います。

generated class は `public partial class` です。extension point として、`DelegatingHandler` chain を注入する `public {ClassName}(HttpMessageHandler handler)` constructor overload と、各 request 送信前に呼ばれる `partial void OnRequestPreparing(HttpRequestMessage request)` hook があります。

`IHttpClientFactory` と統合する場合は composition root で named client を登録し、resolved `HttpClient` を constructor に渡します。

```csharp
// Registration (ASP.NET host or Blazor WASM)
builder.Services.AddHttpClient(nameof(SliceApiClient), c => c.BaseAddress = new Uri("https://api.example.com"))
    .AddHttpMessageHandler<YourDelegatingHandler>();
builder.Services.AddScoped(sp =>
    new SliceApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SliceApiClient))));
```

### Trim/AOT-safe generation

generated client は reflection-based overload ではなく、trim-safe な `JsonTypeInfo<T>` overload (`ReadFromJsonAsync(JsonTypeInfo<T>, ct)`, `JsonContent.Create(value, JsonTypeInfo<T>)`, `JsonSerializer.Deserialize(body, JsonTypeInfo<T>)`) を使います。既定では generated file 末尾に internal `{ClassName}JsonContext : JsonSerializerContext` を auto-emit し、request/response type と `SliceProblemDetails` を登録します。これにより追加設定なしで `PublishTrimmed=true` / `PublishAot=true` に対応します。

独自の `JsonSerializerContext` を使いたい場合は `--json-context` を渡します。

```bash
slicefx client csharp --json-context My.App.MyJsonContext --output SliceApiClient.g.cs
```

`--json-context` 指定時は auto-emitted context が抑止され、user-provided FQN が参照されます。context は全 route の request/response type と `{YourClassName}.SliceProblemDetails` を登録し、以下の `TypeInfoPropertyName` convention に合わせる必要があります。

| Type shape | Property name |
|---|---|
| Simple type `Foo.Bar.MyResponse` | `MyResponse` |
| last segment が `Request` / `Response` の type | `{ParentSegment}Request` / `{ParentSegment}Response` |
| `IReadOnlyList<X>` / `IList<X>` / `List<X>` / `IEnumerable<X>` / `X[]` | `{X}List` |
| `IReadOnlyDictionary<K,V>` / `Dictionary<K,V>` | `{V}Dictionary` |
| `{ClassName}.SliceProblemDetails` | `SliceProblemDetails` |
| Other generic | CLI error。`--json-context` を使います。 |

## Typed TypeScript client

`slicefx client typescript` は portable / partial route 向けに zero-dependency な `fetch`-based TypeScript client を生成します。build output で property information が利用できる request/response record shape について TypeScript interface を emit します。schema reader は `[JsonPropertyName]`、`[JsonIgnore]`、required member、string-enum converter、base64 string として表現される binary member などの common `System.Text.Json` metadata を尊重します。

generated code は global `fetch` API だけに依存し、browser、Cloudflare Workers、Node.js 18+、Deno で動きます。

```typescript
const client = new SliceApiClient("https://api.example.com", {
  headers: { Authorization: `Bearer ${token}` }
});
const item = await client.items.getItemAsync(42);
```

`aspnet-only` route は generated TypeScript / C# client から除外されます。必要な場合は standard OpenAPI toolchain または manual ASP.NET-specific client を使います。

## OpenAPI manifest projection

`slicefx openapi` は source-generated route manifest から OpenAPI JSON document を書き出します。CI、WASI、Lambda function-per-feature など、ASP.NET host を起動せずに portable contract が欲しい場合のためのものです。

```bash
slicefx openapi --output openapi.json
slicefx openapi --title SliceFx.Sample --version 1.0.0 --output openapi.json
```

document には `x-slicefx-source: "manifest"` が付きます。hosted ASP.NET app では `builder.Services.AddOpenApi()` / `app.MapOpenApi()` による runtime document が authoritative です。

既定では `portable` と `partial` route を含めます。`aspnet-only` route は `IResult` response schema を安全に推論できないため省略し、warning と `x-slicefx-omitted` に記録します。incomplete schema と明示的な `x-slicefx-portability` metadata を許容する場合だけ `--include-aspnet-only` を使います。

## AWS Lambda artifacts

`slicefx manifest aws-lambda` は source-generated route manifest を読み、AWS SAM `template.yaml` を書きます。

既定 (`--mode hosted`) では、ASP.NET-hosted Slice app 用の `AWS::Serverless::Function` を1つ emit し、各 `[Feature]` に API Gateway `HttpApi` event を付けます。`SliceFx.Lambda` は ASP.NET Core hosting を通るため全 feature を含みます。

`--mode function-per-feature` は eligible generated `SliceFx.Lambda.FunctionPerFeature` handler ごとに `AWS::Serverless::Function` を emit し、unsupported route は理由付きで除外します。各 function は独自の NativeAOT custom-runtime artifact を指します。

```bash
slicefx package aws-lambda --mode function-per-feature --rid linux-x64 --output artifacts/aws-lambda
```

`--skip-publish` を使うと `dotnet publish` を実行せずに per-feature wrapper project を emit し、eligibility を検証できます。

```bash
slicefx package aws-lambda --mode function-per-feature --skip-publish --output artifacts/aws-lambda
```

command は package `obj` directory 配下に eligible route ごとの temporary entry project を生成し、各 route を個別 artifact directory に publish し、feature ごとに package manifest artifact を書きます。NativeAOT publish を route 数だけ実行するため時間がかかることがあります。

`slicefx-lambda-package-report.json` には artifact size、top files、binlog path、warning details、mstat/map path、closure inspection result が含まれます。`--warning-baseline` なしでは real publish は warning 0 を要求します。closure inspection は sibling feature entrypoint や app-wide registration surface などが artifact に root された場合に失敗します。

`slicefx package` surface は現在 AWS Lambda function-per-feature artifact のみです。WASI deployment は `dotnet publish -r wasi-wasm` で1つの `wasi:http` component を作る flow のままです。
