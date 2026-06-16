# Parameter binding across hosting targets

[English](../../guides/parameter-binding.md) | [日本語 docs index](../README.md)

> この日本語版は参考訳です。仕様判断は英語版を正本とします。

SliceFx は同じ `Handle` signature を supported host ごとに map します。ただし host ごとに parameter resolution が異なるため、binding rule も target によって変わります。重要な違いは以下です。

| 観点 | ASP.NET Core (JIT / Kestrel) | ASP.NET NativeAOT (`SliceAspNetAot`) | Portable (WASI / Lambda) |
|---|---|---|---|
| binding の決定主体 | ASP.NET Core **runtime** binder | source generator at **compile time** | source generator at **compile time** |
| concrete DI service, no attribute | `IServiceProviderIsService` 経由で DI 解決（JSON-context 不問） | JSON-context 登録 + body verb → body candidate → **SLICE070 Error**（要 annotation） | body candidate とみなされ route excluded（**SLICE023/033**）（要 annotation） |
| `[FromServices]` は必要か | 不要。optional / harmless | concrete service が JSON context に載っており body verb なら必要 | concrete service type では必要 |

## ASP.NET Core (Kestrel / TestHost)

source generator は bare delegate を使った plain `MapMethods` call を emit します。binding annotation は付与しません。binding は **ASP.NET Core の推論と完全に同じ** で、順序は以下です。

1. explicit attribute (`[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`, `[FromServices]`, `[FromKeyedServices]`)
2. special type (`HttpContext`, `HttpRequest`, `HttpResponse`, `CancellationToken`, ...)
3. `TryParse` を持つ simple type → matching `{token}` があれば route、それ以外は query string
4. DI container に登録されている type — concrete / interface どちらも service として解決。ASP.NET Core は `IServiceProviderIsService` を使います。
5. remaining complex type が1つだけなら JSON request body

これは **raw Minimal API と同一** です。したがって ASP.NET path では registered service に `[FromServices]` は **不要** です。付けても binding への影響はありません。

```csharp
// AuditLog が AddSingleton<AuditLog>() されていれば、ASP.NET では [FromServices] なしで動く
public static Task<Response> Handle(Request req, AuditLog audit, CancellationToken ct) { ... }
```

generated DataAnnotations validation filter は runtime の `IServiceProviderIsService` で gate し、registered service として解決される parameter は skip します。つまり ASP.NET が request body から bind する parameter だけを validate します。`ISliceValidator<T>` filter は compile time に request-like (body) parameter にだけ attach されます。

## ASP.NET NativeAOT (`[assembly: SliceAspNetAot]`)

`[assembly: SliceAspNetAot]` を付けると、source generator は ASP.NET 登録パスも **compile-time binding** に切り替えます（runtime の `IServiceProviderIsService` 推論は使用しません）。binding convention は Portable dispatch と同じ `ResolveConventionBinding`（`SourceGenerationHelpers.cs`、`AspNetAotRegistrationEmitter` で共用）で、以下の順序で解決します。

1. explicit attribute（`[FromRoute]`、`[FromQuery]`、`[FromHeader]`、`[FromBody]`、`[FromServices]`、`[FromKeyedServices(key)]`）— そのまま尊重
2. `CancellationToken` → service
3. interface / abstract complex type → DI service（annotation 不要）
4. **`[SliceJsonContext(AspNet)]` に登録された concrete 非 framework 型 が POST / PUT / PATCH 上にある → request body candidate**
   - JSON context に登録されていない concrete 型 → verb に関わらず常に DI service（診断なし）
   - GET / HEAD / DELETE（body なし）上の concrete 型 → 常に DI service（診断なし）
5. simple type → matching `{token}` があれば route、それ以外は query string

body candidate が2つになると **SLICE070（Error）** が出ます（`"multiple body parameters are not supported"`）。WASI の SLICE023（Warning）/ Lambda の SLICE033（Warning）とは **ID・severity が異なりビルドエラー**になります。`[FromServices]` を付けるか、interface 型を使うことで回避できます。

```csharp
// NpgsqlDataSource は [SliceJsonContext(AspNet)] に未登録 → GET でも POST でも常に DI service
// → [FromServices] 不要
public static async Task<SliceResult> Handle(NpgsqlDataSource db, CancellationToken ct) { ... }

// AuditLog が [SliceJsonContext(AspNet)] に登録済み かつ POST → body candidate → SLICE070
// 回避: [FromServices] を付けるか interface 型にする
public static async Task<SliceResult<Response>> Handle(
    Request req,
    [FromServices] AuditLog audit,
    CancellationToken ct) { ... }
```

## Portable dispatch (WASI / Lambda function-per-feature)

source generator は runtime service provider を probe できないため、すべての binding を **compile time** に解決します。static convention は以下です。

1. explicit attribute (`[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`, `[FromServices]`, `[FromKeyedServices(key)]`) — そのまま尊重
2. `CancellationToken` → service として解決
3. interface または abstract complex type → DI service として解決
4. **POST / PUT / PATCH 上の concrete, non-framework complex type → request body**
5. simple type → matching `{token}` があれば route、それ以外は query string

annotation なしの concrete DI service は rule 4 により body candidate になります。request DTO など別の body-eligible parameter もある場合、body candidate が2つになります。multiple body parameter は supported ではないため、**feature 全体が portable route table から除外**されます。

- WASI → **SLICE023**
- Lambda function-per-feature → **SLICE033**

`[FromServices]` は parameter を Services に再分類し、body を1つだけに保つため、route を portable にできます。

```csharp
// ASP.NET, WASI, Lambda across portable
public static async Task<Response> Handle(
    Guid id,
    Request req,
    [FromServices] AuditLog audit,           // concrete は portability のため annotate
    [FromKeyedServices("promotion")] IClock clock,
    CancellationToken ct) { ... }
```

full example は `samples/SliceFx.Sample/Features/Users/PromoteUser.cs` を参照してください。

## Recommendation

handler を ASP.NET、WASI、Lambda across portable にしたい場合だけ、concrete service parameter に `[FromServices]`、keyed service に `[FromKeyedServices(key)]` を付けます。ASP.NET-only handler では optional です。interface / abstract-typed dependency はすべての path で DI と推論され、annotation は不要です。
