# Handler return-type style guide

[English](../../guides/return-types.md) | [日本語 docs index](../README.md)

> この日本語版は参考訳です。仕様判断は英語版を正本とします。

Slice `Handle` method は plain response type または `IResult` を返せます。この guide はどちらを選ぶべきかを説明します。

## Recommendation up front

**既定では response type を直接返してください。** custom status code、header、empty body など HTTP detail が本当に重要な場合だけ `IResult` を選びます。

## Comparison

### Direct response (preferred)

```csharp
[Feature("POST /users", Summary = "Create a new user")]
public static class CreateUser
{
    public record Request([Required, MinLength(2)] string Name, [Required, EmailAddress] string Email);
    public record Response(Guid Id, string Name, string Email, DateTime CreatedAt);

    public static async Task<Response> Handle(Request req, IUserStore store, CancellationToken ct)
    {
        var user = await store.AddAsync(req.Name, req.Email, ct).ConfigureAwait(false);
        return new Response(user.Id, user.Name, user.Email, user.CreatedAt);
    }
}
```

advantages:

- **WASI-portable** — `slicefx routes` はこれを `portable` と分類します。
- **直接 test しやすい** — `var r = await CreateUser.Handle(...); Assert.Equal(...)`。
- **正確な OpenAPI schema** — Minimal API が response type を推論します。

WASI や function-per-feature Lambda のような generated non-ASP.NET path では、direct typed return value の `null` は JSON value として扱われます。response は `200 application/json`、body は `null` です。`null` が "not found" や "no content" を意味する場合は explicit platform-specific result を返してください。WASI では `WasiResults.NotFound()` や `WasiResults.NoContent()`、function-per-feature Lambda では `APIGatewayHttpApiV2ProxyResponse` を直接返します。

example: `samples/SliceFx.Sample/Features/Users/CreateUser.cs`

### `IResult` (when HTTP details matter)

```csharp
[Feature("GET /users/{id:guid}", Summary = "Get a user by id")]
public static class GetUser
{
    public record Response(Guid Id, string Name, string Email, DateTime CreatedAt);

    public static async Task<IResult> Handle(Guid id, IUserStore store, CancellationToken ct)
    {
        var user = await store.GetAsync(id, ct).ConfigureAwait(false);
        return user is null
            ? Results.NotFound()
            : Results.Ok(new Response(user.Id, user.Name, user.Email, user.CreatedAt));
    }
}
```

`IResult` が妥当なケース:

- 404 / 401 / 204 — response type だけでは表現できない status code。
- imperative に設定する必要がある custom header。
- binary / streaming response。

example: `samples/SliceFx.Sample/Features/Users/GetUser.cs`

## Trade-offs to know

| Concern | Direct response | `IResult` return |
|---|---|---|
| WASI portability | ✅ `portable` | ❌ `aspnet-only` (SLICE020 info) |
| OpenAPI schema fidelity | ✅ return type が反映される | ⚠️ schema を回復するには `TypedResults.Ok<T>()` を使う |
| Custom HTTP status | ❌ 常に 200 / 204 | ✅ 任意 |
| Test ergonomics | ✅ plain value を assert | ⚠️ response を読むには `IResult.ExecuteAsync` が必要 |

## Recommended patterns

1. **Default: direct response.**
2. **404 / 401 などが必要な場合は `IResult` を使う。** endpoint が WASI-portable ではなくなる点を受け入れるか、WASI path では `WasiResponse` に切り替えます。
3. **generated non-ASP.NET path では adapter-specific response type を使う。** `WasiResponse` は WASI dispatch で pass-through され、`APIGatewayHttpApiV2ProxyResponse` は function-per-feature Lambda handler で pass-through されます。
4. **HTTP-shaped return と OpenAPI type の両方が欲しい場合は `TypedResults.Ok<Response>()` を使う。** 現在の generator は `TypedResults` を含むすべての `IResult` implementation を `aspnet-only` として扱います。`partial` は endpoint filter など ASP.NET-only attached behavior を持つ portable route shape に使われます。

## When SLICE020 appears

`IResult` を返すことは意図的な trade-off です。SLICE020 info message は「この endpoint は WASI route から除外される」という reminder であり、必ず修正が必要なわけではありません。WASI path に載せたい場合は direct response に切り替えてください。
