# ASP.NET features and escape hatches

[English](../../guides/aspnet-features.md) | [日本語 docs index](../README.md)

> この日本語版は参考訳です。仕様判断は英語版を正本とします。

SliceFx generated code は pure Minimal API expansion です。すべての feature endpoint は standard `WebApplication.MapMethods` call であり、ASP.NET Core の surface はそのまま利用できます。framework は使える ASP.NET feature を制限しません。

## What you keep

以下の機能は追加設定なしで SliceFx feature endpoint に使えます。

| Feature | How to use |
| --- | --- |
| ASP.NET Core Authorization | `[Authorize]`、policies、`RequireAuthorization()`、fallback policy |
| Output caching | route group 経由の `CacheOutput()` |
| Rate limiting | route group 経由の `RequireRateLimiting()` |
| CORS | route group 経由の `RequireCors()` |
| Exception handling | exception handling middleware と `IProblemDetailsService` |
| Endpoint filters | `[Filter<T>]` が standard `IEndpointFilter` を宣言順に適用 |
| OpenAPI | `builder.Services.AddOpenApi()` / `app.MapOpenApi()` |
| Standard binding | `[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromForm]`, `[FromServices]`, `[FromKeyedServices]` |

## Route groups for cross-cutting policies

route group は別 middleware pipeline を作らず、ASP.NET endpoint metadata と convention を scope します。group に適用した policy は、その group 経由で map された全 slice に適用されます。

```csharp
app.MapGroup("/api")
   .RequireAuthorization("AdminPolicy")
   .RequireRateLimiting("sliding")
   .MapSlices();
```

## Escape hatches

### Validation

**Default path:** request record 上の DataAnnotations attribute — `[Required]`、`[MinLength]`、`[EmailAddress]` など。supported attribute は compile-time validation として生成され、endpoint filter より前に実行されます。

supported attribute: `Required`、`StringLength`、`MinLength`、`MaxLength`、`EmailAddress`、`Url`、`RegularExpression`、`Range`。

**Escape hatch:** cross-field rule、async check、custom attribute、DI が必要な rule には `ISliceValidator<TRequest>` を実装します。

```csharp
public sealed class CreateUserValidator : ISliceValidator<CreateUser.Request>
{
    private readonly IUserStore _store;

    public CreateUserValidator(IUserStore store) => _store = store;

    public async ValueTask<SliceValidationResult> ValidateAsync(CreateUser.Request value, CancellationToken ct)
    {
        if (await _store.EmailExistsAsync(value.Email, ct))
            return SliceValidationResult.Failure("Email", "Email is already registered.");
        return SliceValidationResult.Success;
    }
}
```

generator は validator を自動で発見・登録します。generated DataAnnotations check が先に走り、その後 `ISliceValidator<T>`、最後に `[Filter<T>]` filter が走ります。

背景は [Design decisions FAQ](../design-decisions.md#なぜ-dataannotations-と-islicevalidatort-の両方があるのか) を参照してください。

### Authorization

**Default path:** endpoint 上の `[Authorize]` または group-level `RequireAuthorization()`。

**Escape hatch:** `builder.Services.AddAuthorizationBuilder()` による fallback authorization policy、または route group 経由の per-endpoint `RequireAuthorization("PolicyName")`。

```csharp
// Fallback: require authentication for all endpoints except those explicitly allowing anonymous
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
```

`[Filter<T>]` を authorization system の代替として使わないでください。Slice filter は明示的な per-feature endpoint behavior 用であり、security boundary 用ではありません。

### Rate limiting, output caching, CORS

これらの policy の idiomatic scope は route group です。

```csharp
app.MapGroup("/api")
   .RequireRateLimiting("sliding")
   .CacheOutput("default")
   .RequireCors("AllowFrontend")
   .MapSlices();
```

### Cross-cutting behavior

**Default path:** logging、API-key check、audit など per-feature concern には `[Filter<T>]` endpoint filter。

**Escape hatch:** 全 request または non-SliceFx endpoint にも適用する concern には standard ASP.NET Core middleware。

## DI binding

ASP.NET path では generated code は plain Minimal API です。binding annotation は注入されません。ASP.NET Core は built-in `IServiceProviderIsService` check により、登録済み service (concrete / interface) を DI から解決します。`[FromServices]` はここでは不要で、raw Minimal API と同じ動きです。

> **Portability note:** ASP.NET、WASI、Lambda across portable にしたい場合は、concrete service parameter に `[FromServices]`、keyed service に `[FromKeyedServices(key)]` を付けてください。portable-dispatch generator は compile-time heuristic を使い DI container を probe できないため、annotation なしの concrete service は second body candidate とみなされ、portable route table から除外されます (SLICE023/SLICE033)。
>
> 詳細は [Parameter binding across hosting targets](parameter-binding.md) を参照してください。

`samples/SliceFx.Sample/Features/Users/PromoteUser.cs` がこの pattern を示しています。
