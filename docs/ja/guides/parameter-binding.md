# Parameter binding across hosting targets

[English](../../guides/parameter-binding.md) | [日本語 docs index](../README.md)

> この日本語版は参考訳です。仕様判断は英語版を正本とします。

SliceFx は同じ `Handle` signature を supported host ごとに map します。ただし host ごとに parameter resolution が異なるため、binding rule も target によって変わります。重要な違いは以下です。

| 観点 | ASP.NET Core (JIT / Kestrel) | ASP.NET NativeAOT (`SliceAspNetAot`) | Portable (WASI / Lambda) |
|---|---|---|---|
| binding の決定主体 | ASP.NET Core **runtime** binder | source generator at **compile time** | source generator at **compile time** |
| concrete DI service, no attribute | `IServiceProviderIsService` 経由で DI 解決（JSON-context 不問） | body verb 上で唯一の request-like candidate であり JSON context に登録されている場合を**除き** DI から解決される。それ以外は body になる（[body selection](#body-selection-compile-time-paths) 参照） | ASP.NET NativeAOT と同じ precedence — JSON context 上の唯一の body-verb candidate でなければ DI |
| `[FromServices]` は必要か | 不要。optional / harmless | 上記の residual case のみ必要 | 上記の residual case のみ必要 |

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

`[assembly: SliceAspNetAot]` を付けると、source generator は ASP.NET 登録パスも **compile-time binding** に切り替えます（runtime の `IServiceProviderIsService` 推論は使用しません）。binder は各 parameter の source を以下の順序で解決します（`SourceGenerationHelpers.cs` の `ResolveConventionBinding`、`AspNetAotRegistrationEmitter` で共用）。

1. explicit attribute（`[FromRoute]`、`[FromQuery]`、`[FromHeader]`、`[FromBody]`、`[FromServices]`、`[FromKeyedServices(key)]`）— そのまま尊重
2. `CancellationToken` → service として解決
3. interface / abstract complex type → DI service として解決（annotation 不要）
4. 残った concrete・非 framework complex parameter のうち **最大1つ**が request body として選ばれます。選定は下記 [body selection](#body-selection-compile-time-paths) の precedence に従います。body として選ばれなかった concrete parameter はすべて DI から解決されます。
5. simple type → matching `{token}` があれば route、それ以外は query string

nested `Request` record と任意個数の injected concrete service を持つ handler は、annotation なしでコンパイルが通ります — nested type が body slot を獲得し（precedence 2）、残りはすべて DI にフォールスルーします。**SLICE070（Error）** が発火するのは、選定が本当に ambiguous な場合、つまり2つの candidate が同じ precedence level で衝突する場合（例: `[FromBody]` が2つ、nested type が2つ、あるいは sole JSON-context candidate が2つ）だけです。診断は2番目の candidate を名指しし、「second request-body candidate; a handler binds at most one request body」（1つの handler が bind できる request body は最大1つであり、これは2番目の request-body candidate である）と述べます — 意図する body に `[FromBody]` を付け、もう一方に `[FromServices]` を付けるか、interface/abstract 型にして candidate を1つに絞ってください。WASI の SLICE023（Warning）/ Lambda の SLICE033（Warning）とは異なり、SLICE070 は **Error** であり、ビルドが失敗します。

```csharp
// [assembly: SliceAspNetAot] が設定されている場合の compile-time binding。
// NpgsqlDataSource は [SliceJsonContext(AspNet)] に未登録で、feature の nested type でもない
// → 常に DI service。[FromServices] は不要。
public static async Task<SliceResult> Handle(NpgsqlDataSource db, CancellationToken ct) { ... }

// 慣習的な形: Request（nested type）が precedence 2 で body slot を獲得する。
// AuditLog は [SliceJsonContext(AspNet)] に登録されていても DI から解決される
// — nested Request が選ばれた時点で sole request-like candidate ではなくなるため。
// このまま annotation なしでコンパイルできる。
public static async Task<SliceResult<Response>> Handle(
    Request req,
    AuditLog audit,
    CancellationToken ct) { ... }

// 2つの candidate が衝突（nested Request がなく tie を破れない、両方 JSON context 登録済み）→ SLICE070。
// 回避: interface 型を使うか、body ではない側に [FromServices] を付ける。
public static async Task<SliceResult<Response>> Handle(
    CreateOrderPayload payload,
    [FromServices] AuditLog audit,   // [FromServices] がないと POST で SLICE070
    CancellationToken ct) { ... }
```

## Portable dispatch (WASI / Lambda function-per-feature)

source generator は runtime service provider を probe できないため、すべての binding を **compile time** に解決します。ASP.NET NativeAOT と同じ `SelectBodyParameter` precedence（下記 [body selection](#body-selection-compile-time-paths) 参照）を適用します。

1. explicit attribute (`[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`, `[FromServices]`, `[FromKeyedServices(key)]`) — そのまま尊重
2. `CancellationToken` → service として解決
3. interface または abstract complex type → DI service として解決
4. 残った concrete・非 framework complex parameter のうち **最大1つ**が request body として選ばれます（nested `Request` type を最優先、次に sole JSON-context-registered candidate、いずれも POST/PUT/PATCH のみ）。他の concrete parameter はすべて DI から解決されます。
5. simple type → matching `{token}` があれば route、それ以外は query string

nested `Request` record と injected concrete service を持つ handler は、`[FromServices]` annotation なしで portable になります — nested type が body slot を獲得し、残りは DI にフォールスルーします。ambiguity が生じるのは、2つの parameter が同じ precedence で衝突する場合だけです（例: nested type がなく tie を破れない、JSON-context-registered candidate が2つ）。この場合、**feature 全体が portable route table から除外**されます。

- WASI → **SLICE023**（`WasiRegistrationEmitter.cs`、Warning）
- Lambda function-per-feature → **SLICE033**（`LambdaFunctionPerFeatureEmitter.cs`、Warning）

ASP.NET NativeAOT path の SLICE070 とは異なり、SLICE023/SLICE033 のメッセージは除外された parameter/type を報告しますが、"second request-body candidate" のような詳細な文言ではありません — これらは Warning であり、ビルドを止める Error ではありません。`[FromServices]` は衝突している parameter を Services に再分類し（rule 1）、body candidate を1つだけに保つことで route を portable にします。

```csharp
// ASP.NET, WASI, Lambda を通じて portable
public static async Task<Response> Handle(
    Guid id,
    Request req,                                    // nested type が body slot を獲得
    [FromServices] AuditLog audit,                   // concrete — いずれにせよ DI から解決される;
                                                      // [FromServices] は意図を明示するためのもので、
                                                      // AuditLog が唯一残る request-like candidate に
                                                      // なる場合のみ必須
    [FromKeyedServices("promotion")] IClock clock,    // keyed service
    CancellationToken ct) { ... }
```

full example は `samples/SliceFx.Sample/Features/Users/PromoteUser.cs` を参照してください（その doc comment はこの precedence 変更以前のもので `[FromServices]` を必須と記述していますが、そのままでも正しく portable です — ただし convention の最小例ではなくなっています）。

## Body selection (compile-time paths)

ASP.NET NativeAOT、WASI、Lambda function-per-feature は1つの selector（`SourceGenerationHelpers.cs` の `SelectBodyParameter`）を共有しており、handler ごとに **最大1つ**の request body を解決します。適用順序は以下です。

1. **`[FromBody]`** — 明示的に annotate された parameter。HTTP method を問わない。
2. **Convention** — `POST`/`PUT`/`PATCH` 上で、型が feature class に nest されている parameter（慣習的な `Request` record）。
3. **Sole serializable candidate** — `POST`/`PUT`/`PATCH` 上で、`[SliceJsonContext]` に登録された残り唯一の request-like parameter（generated client が使う non-nested な共有 contract をカバーする）。
4. **それ以外 → DI**。`GET`/`DELETE` handler もすべてここに含まれる（body を推論しない）。

body として選ばれなかった parameter はすべて DI から解決されます。interface、abstract type、`[FromServices]` が付いた parameter は常に DI であり、body candidate の集合には決して入りません。

false な second-body error を避けるために、injected service をすべて interface にしたり、plain value を `IConfiguration` で wrap したりする必要はもうありません — nested `Request` と並ぶ injected concrete type は自動的に DI から解決されます（precedence 2 が既に body slot を獲得しているため）。診断が出るのは **本当に tie している場合**だけです — つまり同じ precedence level の2つの candidate があり、それより前の precedence で tie を破れない場合です。診断は `[FromBody]`/`[FromServices]` での disambiguation を求めます: SLICE070（ASP.NET NativeAOT、Error）がこの詳細な文言を持ち、SLICE023（WASI）/SLICE033（Lambda function-per-feature）は route を除外するだけの Warning で、メッセージはそこまで詳細ではありません。

**Residual cases:**

- concrete type が DI service **かつ** `[SliceJsonContext]` に登録されており、body verb 上で **唯一の** request-like parameter である場合、body として扱われます（precedence 3）— 代わりに DI から解決させたい場合は `[FromServices]` を付けるか interface に変更してください。
- body として選ばれた型が DI に未登録の場合、コンパイルは成功しますが、runtime の DI 解決で失敗します — raw ASP.NET Core Minimal API と同様です。選ばれた body type が実際に登録されているかを確認する compile-time check はありません。

## Recommendation

body には nested `Request` convention に頼り、concrete service は暗黙的に DI から解決させましょう — これは ASP.NET、WASI、Lambda を通じて annotation なしで portable です。concrete service に `[FromServices]`（keyed service には `[FromKeyedServices(key)]`）を付けるのは、それが body slot で tie してしまう場合（上記の residual case 参照）か、意図を明示的に文書化したい場合だけにしてください。interface / abstract-typed dependency はすべての path で常に DI から推論され、annotation は不要です。
