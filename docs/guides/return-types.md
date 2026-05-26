# Handler return-type style guide

A Slice `Handle` method can return either a plain response type or an `IResult`. This guide explains when to choose each.

## Recommendation up front

**Default to returning a response type directly.** Reach for `IResult` only when HTTP details (custom status code, headers, empty body) genuinely matter.

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

Advantages:

- **WASI-portable** — `slicefx routes` classifies this as `portable`.
- **Direct testability** — `var r = await CreateUser.Handle(...); Assert.Equal(...)`.
- **Accurate OpenAPI schema** — Minimal API infers the response type.

For generated non-ASP.NET paths such as WASI and function-per-feature Lambda, a direct
typed return value of `null` is still a JSON value: the response is
`200 application/json` with body `null`. If `null` means "not found" or "no
content", return an explicit platform-specific result instead. In WASI, use
`WasiResponse` helpers such as `SliceResult.NotFound()` or
`SliceResult.NoContent()`. In function-per-feature Lambda, return
`APIGatewayHttpApiV2ProxyResponse` directly.

See `samples/SliceFx.Sample/Features/Users/CreateUser.cs`.

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

Legitimate use cases for `IResult`:

- 404 / 401 / 204 — status codes that a response type cannot express.
- Custom headers that need to be set imperatively.
- Binary or streaming responses.

See `samples/SliceFx.Sample/Features/Users/GetUser.cs`.

## Trade-offs to know

| Concern | Direct response | `IResult` return |
|---|---|---|
| WASI portability | ✅ `portable` | ❌ `aspnet-only` (SLICE008 info) |
| OpenAPI schema fidelity | ✅ Return type flows through | ⚠️ Use `TypedResults.Ok<T>()` to recover the schema |
| Custom HTTP status | ❌ Always 200 / 204 | ✅ Anything |
| Test ergonomics | ✅ Assert plain values | ⚠️ Need `IResult.ExecuteAsync` to read the response |

## Recommended patterns

1. **Default: direct response.**
2. **Reach for `IResult` when 404 / 401 / etc. are required**, accepting that the endpoint is no longer WASI-portable (or switch to `WasiResponse`).
3. **Use adapter-specific response types for generated non-ASP.NET paths.** `WasiResponse` is passed through by WASI dispatch, and `APIGatewayHttpApiV2ProxyResponse` is passed through by function-per-feature Lambda handlers.
4. **Use `TypedResults.Ok<Response>()` when you want both an HTTP-shaped return and OpenAPI types.** The current generator treats all `IResult` implementations, including `TypedResults`, as `aspnet-only`; `partial` is used for portable route shapes with ASP.NET-only attached behavior such as endpoint filters.

## When SLICE008 appears

Returning `IResult` is a deliberate trade-off; the SLICE008 info message is a reminder that "this endpoint is excluded from WASI routes". No fix is required. If you do want it on the WASI path, switch to a direct response.
