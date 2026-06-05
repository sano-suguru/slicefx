# Source generator and route manifest

[English](../source-generator.md) | [日本語 docs index](README.md)

> この日本語版は参考訳です。仕様判断は英語版を正本とします。

`SliceFx.SourceGenerator` は SliceFx feature の registration path です。compile time に `[Feature]` class を発見し、`SliceFx` namespace に明示的な ASP.NET Core Minimal API registration を emit します。

## Generated registration shape

generated `MapSlices()` method は各 feature を `MapMethods` で map し、validation、feature filter、endpoint metadata を付与します。

```csharp
public static IEndpointRouteBuilder MapSlices(this IEndpointRouteBuilder app)
{
    app.MapMethods(
            "/users",
            new[] { "POST" },
            new Func<CreateUser.Request, IUserStore, CancellationToken, Task<CreateUser.Response>>(CreateUser.Handle))
        .AddEndpointFilterFactory(__CreateDataAnnotationsValidationFactory_CreateUser)
        .WithTags("Users")
        .WithName("Users.CreateUser");

    return app;
}
```

generated validation は supported DataAnnotations rule がある場合だけ emit されます。対象は `Required`、length/range rule、`EmailAddress`、`Url`、`RegularExpression` です。support は shape-conditional で、`StringLength` は `string` property のみ、`Range` は numeric type のみです。resource/localized error message を持つ attribute は type に関係なく unsupported として扱います。

custom `ValidationAttribute`、type-level validation、`IValidatableObject`、resource-based message など reflection-bound validation が必要な shape は ASP.NET registration では SLICE010 として報告されます。default registration を reflection-free / trimming-friendly に保つため、そうした rule は `ISliceValidator<TRequest>` へ移してください。

## Multi-assembly apps

feature assembly は generated module helper と assembly marker を公開します。host assembly は user-facing な `AddSlice()` / `MapSlices()` extension を emit し、runtime scanning なしで directly referenced Slice module を明示的に aggregate できます。

class library project は既定で module helper のみを emit します。executable host は public extension surface を emit し、host project 内の feature だけを map します。必要な場合だけ `SliceFxRole` を `Host`、`Feature`、`Both` に設定します。

referenced module aggregation は MSBuild property で制御します。

```xml
<PropertyGroup>
  <!-- Preferred: aggregate only these referenced assembly simple names. -->
  <SliceFxReferencedAssemblies>FeatureLib;SharedSlices</SliceFxReferencedAssemblies>

  <!-- Optional migration switch: aggregate every directly referenced Slice module. -->
  <SliceFxAggregateReferences>true</SliceFxAggregateReferences>
</PropertyGroup>
```

host が Slice feature assembly を参照しているのに `SliceFxReferencedAssemblies` も `SliceFxAggregateReferences` も設定していない場合、generator は SLICE050 を報告し、host local-only のままにします。local-only を明示して diagnostic を抑止したい場合は `SliceFxAggregateReferences=false` を設定します。

generator は host registration を emit する前に、local feature と aggregated referenced module 間の endpoint-name uniqueness を検証します。

## Route manifest

generator は feature が1つもないプロジェクトに対しても empty manifest を含む route metadata を emit します。manifest には以下が含まれます。

- HTTP method と route pattern
- feature type、tag、endpoint name、summary
- request type と return type
- handler parameter names
- referenced filter type names
- portability status: `portable`、`partial`、`aspnet-only`

manifest は string-based です。tool は `SliceFx.Core` に dependency を足さずに route shape を読めます。`slicefx openapi` は manifest を offline OpenAPI projection として使います。hosted ASP.NET app では `Microsoft.AspNetCore.OpenApi` の runtime document を authoritative としてください。

## Diagnostics

invalid feature shape は compile time に `SLICE###` diagnostic として報告されます。prefix は feature slice の domain term として保持し、framework/package identity は `SliceFx` です。

| Range | Area |
| --- | --- |
| `SLICE001`-`SLICE009` | Core feature shape, routing, endpoint metadata, filters |
| `SLICE010`-`SLICE019` | Validation |
| `SLICE020`-`SLICE029` | WASI portability |
| `SLICE030`-`SLICE039` | Lambda function-per-feature |
| `SLICE040`-`SLICE049` | JSON context overrides |
| `SLICE050`-`SLICE059` | Cross-assembly aggregation |
| `SLICE060`-`SLICE069` | Minimal API migration overlap |

| ID | Severity | Area | Meaning | Suggested fix |
| --- | --- | --- | --- | --- |
| `SLICE001` | Error | Core feature shape | feature type に `Handle` method がありません。 | `public static Handle(...)` method を1つ追加します。 |
| `SLICE002` | Error | Core feature shape | `Handle` が public static ではありません。 | handler を `public static` にします。 |
| `SLICE003` | Error | Core feature shape | 複数の `Handle` method により feature が曖昧です。 | feature type ごとに handler は1つにします。 |
| `SLICE004` | Error | Routing | route が `METHOD /path` 形式ではありません。 | supported HTTP method と absolute route path を使います。 |
| `SLICE005` | Error | Endpoint metadata | 2つの feature が同じ endpoint name を生成します。 | feature 名を変えるか `FeatureAttribute.Name` / `FeatureAttribute.Tag` を設定します。 |
| `SLICE006` | Info | Endpoint metadata | `.Features.` namespace segment から tag を推論できません。 | `.Features.<Tag>` namespace に移すか `FeatureAttribute.Tag` を設定します。 |
| `SLICE007` | Warning | Filters | `[FilterOrderHint]` が declared filter order と矛盾します。 | hinted dependency が先に実行されるよう filter attribute を並べ替えます。 |
| `SLICE008` | Warning | Filters | `[FilterOrderHint]` が別 execution layer の filter を参照しています。 | same filter type layer 内でのみ hint を使います。 |
| `SLICE010` | Error | Validation | ASP.NET generated registration が reflection-bound DataAnnotations validation を必要とします。 | supported generated validation にするか `ISliceValidator<T>` に移します。 |
| `SLICE011` | Error | Validation | `ISliceValidator<T>` implementation を安全に生成できません。 | concrete request type 向けの closed, constructible implementation にします。 |
| `SLICE012` | Error | Validation | 同じ request に複数の `ISliceValidator<T>` が対応しています。 | rule を1つの validator にまとめます。 |
| `SLICE013` | Error | Validation | validator target type が discovered Slice request parameter と一致しません。 | validator を削除するか、feature handler で使われる request type を target にします。 |
| `SLICE020` | Info | WASI portability | return type が ASP.NET-specific で WASI route table から除外されます。 | POCO、`SliceResult`、`WasiResponse`、`Task<T>`、`ValueTask<T>` を返します。 |
| `SLICE021` | Warning | WASI portability | WASI JSON serialization metadata を安全に生成できません。 | WASI target 向け `JsonSerializerContext` を提供します。 |
| `SLICE022` | Warning | WASI portability | WASI route が reflection-bound DataAnnotations validation を必要とします。 | supported generated validation か `ISliceValidator<T>` を使います。 |
| `SLICE023` | Warning | WASI portability | parameter を WASI route table で bind できません。 | supported route/query/header/body shape を使うか ASP.NET-only にします。 |
| `SLICE030` | Info | Lambda function-per-feature | return type が generated per-feature Lambda handler で supported ではありません。 | POCO、`Task<T>`、`ValueTask<T>`、`APIGatewayHttpApiV2ProxyResponse` を返します。 |
| `SLICE031` | Info | Lambda function-per-feature | endpoint filter は per-feature Lambda path では使えません。 | filter を外すか hosted ASP.NET/Lambda に留めます。 |
| `SLICE032` | Warning | Lambda function-per-feature | Lambda JSON serialization metadata を安全に生成できません。 | Lambda-supported body/response type と generated JSON metadata を使います。 |
| `SLICE033` | Warning | Lambda function-per-feature | parameter を per-feature Lambda handler で bind できません。 | supported route/query/header/body parameter shape を使います。 |
| `SLICE034` | Warning | Lambda function-per-feature | Lambda route が reflection-bound DataAnnotations validation を必要とします。 | supported generated validation か `ISliceValidator<T>` を使います。 |
| `SLICE035` | Error | Lambda function-per-feature | `[LambdaFunctionStartup]` type が invalid です。 | public parameterless type で `ILambdaFunctionPerFeatureStartup` を実装します。 |
| `SLICE036` | Error | Lambda function-per-feature | 2つの feature が同じ Lambda artifact ID を生成します。 | feature name、endpoint name、tag を変更して一意にします。 |
| `SLICE037` | Warning | Parameter binding | `[FromKeyedServices]` key constant を C# literal として再 emit できません。 | string、numeric、bool、char、enum、`typeof` key を使います。 |
| `SLICE040` | Error | JSON context overrides | 同じ Slice adapter に複数の JSON context override があります。 | target ごとに override を1つだけにします。 |
| `SLICE041` | Error | JSON context overrides | explicit JSON context override が `JsonSerializerContext` ではありません。 | `JsonSerializerContext` 派生 type を指定します。 |
| `SLICE050` | Warning | Cross-assembly aggregation | referenced Slice module があるが aggregation が明示されていません。 | `SliceFxReferencedAssemblies`、`SliceFxAggregateReferences=true`、または `false` を設定します。 |
| `SLICE051` | Error | Cross-assembly aggregation | `SliceFxAggregateReferences` が unsupported value です。 | `true`/`false`、`1`/`0`、`yes`/`no` を使います。 |
| `SLICE060` | Warning | Minimal API migration overlap | raw Minimal API route literal が generated Slice route と重複します。 | 片方を削除するか意図的な migration として扱います。 |
| `SLICE061` | Warning | Minimal API migration overlap | raw Minimal API endpoint name が generated Slice endpoint name と重複します。 | endpoint name を変えるか `FeatureAttribute.Name` を設定します。 |
