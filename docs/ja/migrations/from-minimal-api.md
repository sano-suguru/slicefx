# Migrating from plain Minimal APIs

[English](../../migrations/from-minimal-api.md) | [日本語 docs index](../README.md)

> この日本語版は参考訳です。仕様判断は英語版を正本とします。

SliceFx は standard ASP.NET Core Minimal APIs の隣に置けるよう設計されています。1 endpoint ずつ移行し、残りの app はそのままにできます。feature shape が価値を出さない場合は移行を止められます。

> **Preview status:** `0.1.0-preview.8` is available on NuGet。これは pre-1.0 experimental software であり、stable release 前に API が変わる可能性があります。

## When this is a good fit

既存 Minimal API app で endpoint code、DTO、validation、route name、client wiring が散らばり始めている場合に向いています。特に、app 全体に触る前に1つの feature group で形を証明できる small-to-medium API に向いています。

単に "portable" にしたいだけで endpoint を移行しないでください。ASP.NET-only behavior を意図的に使う endpoint は raw Minimal API のままでもよく、Slice feature にして `IResult` を返し `aspnet-only` として扱っても構いません。

## Stage 1: add SliceFx beside existing endpoints

既存 builder style は維持できます。sample では framework が trimming/AOT-oriented であるため `CreateSlimBuilder` を使っていますが、移行 app が SliceFx を試すだけなら builder を切り替える必要はありません。

package を追加します。

```bash
dotnet add package SliceFx.Core --version 0.1.0-preview.8
dotnet add package SliceFx.SourceGenerator --version 0.1.0-preview.8
```

generated namespace 用の global using を追加します。

```xml
<ItemGroup>
  <Using Include="SliceFx" />
</ItemGroup>
```

既存の `MapGet` / `MapPost` の隣で Slice service を登録し、generated route を map します。

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSlice();
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("ok")); // unchanged
app.MapSlices();                               // generated Slice endpoints

app.Run();
```

SliceFx には2つの migration safety net がありますが、route inventory の代わりにはなりません。

- source generator は same-project literal Minimal API overlap を warning として報告します。例: `app.MapPost("/users", ...)` と `[Feature("POST /users")]` が並ぶ場合、また literal `.WithName("...")` が generated Slice endpoint name と一致する場合。
- generated `MapSlices()` は route builder から見える endpoint に対して startup migration audit を実行します。`SliceFx:MigrationAudit` を `Off`、`Warn`、`Throw` にできます。default は Development で `Warn`、それ以外で `Off` です。

移行中は duplicate route を避けます。feature が `POST /users` を map する場合、同じ app で両方を走らせる前に old `app.MapPost("/users", ...)` registration を削除または無効化してください。

## Stage 2: move one endpoint

clear request/response shape を持つ low-risk endpoint から始めます。

Before:

```csharp
app.MapPost("/users", async (
    CreateUserRequest req,
    IUserStore store,
    CancellationToken ct) =>
{
    var user = await store.AddAsync(req.Name, req.Email, ct);
    return Results.Created($"/users/{user.Id}", new CreateUserResponse(
        user.Id,
        user.Name,
        user.Email,
        user.CreatedAt));
})
.WithName("Users.CreateUser")
.WithTags("Users")
.WithSummary("Create a new user");

public record CreateUserRequest(
    [Required, MinLength(2)] string Name,
    [Required, EmailAddress] string Email);

public record CreateUserResponse(Guid Id, string Name, string Email, DateTime CreatedAt);
```

After:

```csharp
using System.ComponentModel.DataAnnotations;

namespace MyApp.Features.Users;

[Feature("POST /users", Name = "Users.CreateUser", Summary = "Create a new user")]
public static class CreateUser
{
    public record Request(
        [Required, MinLength(2)] string Name,
        [Required, EmailAddress] string Email);

    public record Response(Guid Id, string Name, string Email, DateTime CreatedAt);

    public static async Task<Response> Handle(Request req, IUserStore store, CancellationToken ct)
    {
        var user = await store.AddAsync(req.Name, req.Email, ct);
        return new Response(user.Id, user.Name, user.Email, user.CreatedAt);
    }
}
```

この version は handler が `Response` を直接返すため、typed response の `200 OK` になります。`201 Created` の維持が portability より重要なら、HTTP-shaped behavior を維持します。

```csharp
public static async Task<IResult> Handle(Request req, IUserStore store, CancellationToken ct)
{
    var user = await store.AddAsync(req.Name, req.Email, ct);
    return Results.Created($"/users/{user.Id}", new Response(user.Id, user.Name, user.Email, user.CreatedAt));
}
```

この endpoint は通常の ASP.NET Core endpoint のままですが、現在の generator は `IResult` や `TypedResults` return をすべて `aspnet-only` と分類します。`partial` は handler shape は portable だが endpoint filter や reflection-bound validation など attached behavior が portable でない route に使います。詳しくは [return-type guidance](../guides/return-types.md) を参照してください。

## Stage 3: preserve filters, groups, and metadata deliberately

generated Slice registration は standard Minimal API method を呼びます。順序は `MapMethods`、generated validation filter、declared `[Filter<T>]` filter、tag / endpoint name / summary です。

既存と同じ ASP.NET layer を使ってください。

| Existing Minimal API shape | Migration choice |
|---|---|
| logging、CORS、authentication、exception handling など middleware | middleware に残します。 |
| route-group prefix or shared policy | `app.MapGroup("/api").RequireAuthorization().MapSlices();` のように group 経由で map します。route group は endpoint metadata / convention を scope しますが、別 middleware pipeline は作りません。 |
| `RequireAuthorization()` or fallback authorization | middleware または route group の ASP.NET Core authorization policy を優先します。 |
| `[Authorize]` on handler or endpoint | migration 中は group-level `RequireAuthorization(...)` を優先します。attribute に依存する場合は raw endpoint 削除前に runtime OpenAPI と authorization metadata を確認します。 |
| `HttpContext`, `ClaimsPrincipal`, remote IP, headers, route/query/body binding attributes | Minimal API binding が support する限り handler parameter として維持できます。ambient `HttpContext.Items` state に依存する code は contract-test してください。 |
| `[AsParameters]` query/route aggregation | generated delegate でも Minimal API binding が受け入れる場合のみ維持します。そうでなければ parameter を分割するか raw endpoint に残します。 |
| per-endpoint endpoint filter | plain `IEndpointFilter` に変換し、feature に `[Filter<YourFilter>]` を宣言します。 |
| route name, tag, summary | `FeatureAttribute.Name`, `Tag`, `Summary` を使います。endpoint name は既定で `{Tag}.{FeatureClassName}` です。 |
| `CacheOutput()`, `RequireRateLimiting()`, custom `Accepts<T>()` / `Produces<T>()`, custom metadata | explicit route-group policy、typed-result return、または別の replacement ができるまで raw endpoint のままにします。 |
| form/file upload (`IFormFile`, multipart) | exact binding、antiforgery、request-size、OpenAPI shape が重要なら raw のままにします。 |
| custom status code, headers, redirects, files, streams | `IResult` を返して `aspnet-only` を受け入れるか、raw Minimal API のままにします。 |

feature filter は明示的です。SliceFx は source に宣言されていない filter を注入しません。filter 関連は [filter declarations](../../guides/filter-declarations.md) (English) と [filter configuration](../../patterns/filter-configuration.md) (English) を参照してください。

## Stage 4: use tooling after the first slice

少なくとも1つの endpoint を feature にしたら、generated route manifest を使って状態を確認します。

```bash
slicefx routes
slicefx routes --format json
```

portable / partial route は generated client に使えます。

```bash
slicefx client csharp --output SliceApiClient.g.cs
slicefx client typescript --output slice-api-client.ts
```

C# client は handler signature の C# contract type を再利用します。nested feature DTO は endpoint code を local に保ちます。Blazor や別 .NET client が server feature assembly を参照せずに generated client を使う場合は、shared contracts project の non-nested DTO が向いています。

hosted ASP.NET Core OpenAPI では Microsoft の runtime OpenAPI support を使い続けます。CLI OpenAPI output は portable tooling 向けの manifest projection であり、hosted document の replacement ではありません。

client が operation id、status code、request content type、response schema、auth metadata、cache/rate-limit metadata、example に依存している場合は、移行した endpoint ごとに hosted OpenAPI document の before/after を比較してください。`FeatureAttribute.Name` は operation id の維持に役立ちますが、response metadata は handler return type と ASP.NET Core metadata に従います。

## Known migration limits

以下に依存する endpoint は、明示的な replacement ができるまで raw Minimal API のままにします。

- custom metadata convention など、SliceFx が現在 emit しない complex `RouteHandlerBuilder` chain。
- exact binding / OpenAPI shape が重要な form/file upload。
- ASP.NET では動くが route manifest、typed-client generation、WASI、function-per-feature Lambda に意味を持たない custom binding。
- route-group behavior に依存する endpoint filter ordering。
- endpoint-builder call としてだけ存在し、route group や return-type shape に replacement がない authorization、output caching、rate limiting、OpenAPI metadata。
- `IResult` が contract として明確な HTTP result union、custom header、streaming、non-JSON response。

これらは failure ではありません。raw Minimal API と Slice feature を混在させるのが意図された migration mode です。

## Rollback path

SliceFx の generated registration は standard Minimal API registration です。proof of concept が有益でなければ、`Handle` body を `MapGet` / `MapPost` に戻し、`[Feature]` class を削除し、feature が残らなければ `AddSlice()` / `MapSlices()` も削除します。

rollback が機械的なのは server endpoint code だけです。generated client、Postman collection、mock server、OpenAPI snapshot、DTO name/namespace、response shape、dependent branch も変えた場合は、それらも明示的に rollback してください。
