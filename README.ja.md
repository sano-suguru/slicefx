# SliceFx

[English](README.md)

> この日本語版は参考訳です。仕様・リリース情報・セキュリティ上の判断は英語版 `README.md` を正本とします。

[![CI](https://github.com/sano-suguru/slicefx/actions/workflows/ci.yml/badge.svg)](https://github.com/sano-suguru/slicefx/actions/workflows/ci.yml)
[![Perf (nightly)](https://github.com/sano-suguru/slicefx/actions/workflows/perf.yml/badge.svg)](https://github.com/sano-suguru/slicefx/actions/workflows/perf.yml)
[![Pages](https://github.com/sano-suguru/slicefx/actions/workflows/pages.yml/badge.svg)](https://github.com/sano-suguru/slicefx/actions/workflows/pages.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> 1 endpoint = 1 feature file。ASP.NET Core 登録、検査、クライアント、portability hint を生成します。

Website: <https://sano-suguru.github.io/slicefx/>

**SliceFx** は、ASP.NET Core Minimal APIs は好きだが、route string、DTO、validation、filter、client、deployment check をばらばらに保守したくないチーム向けの実験的 .NET framework です。1つの feature は、request、response、handler、validation、filter を1つの static class にまとめます。source generator は標準 Minimal API 登録に加え、tooling、AOT-friendly startup、Lambda 実験、WASI/WebAssembly dispatch 用の route manifest を生成します。

設計背景は [Design decisions FAQ](docs/ja/design-decisions.md) と [Production readiness criteria](docs/ja/production-readiness.md) を参照してください。

## なぜ SliceFx を使うのか

| 課題 | SliceFx が提供するもの |
| --- | --- |
| endpoint code をレビューしやすくしたい | 1 endpoint = 1 feature file。request、response、handler、validation、filter を近くに置けます。 |
| API glue の手同期を減らしたい | `AddSlice()` / `MapSlices()`、route metadata、typed client を同じ feature 定義から生成します。 |
| 標準 ASP.NET Core の挙動を維持したい | Minimal API binding、DI、endpoint filter、DataAnnotations、OpenAPI compatibility、`IResult` をそのまま使えます。 |
| Native AOT-friendly な startup にしたい | generated `MapMethods` が startup route scanning を避けます。`SliceFx.Core` は `PackageReference` を持たず、`Microsoft.AspNetCore.App` framework reference のみです。 |
| portability を早く知りたい | `slicefx routes` が各 endpoint を `portable`、`partial`、`aspnet-only` に分類します。Lambda と wasi:http adapter は opt-in です。 |
| lock-in を低くしたい | 生成コードは標準 `MapMethods` に展開されます。source generator 参照を外して生成結果を展開すれば離脱できます。 |

SliceFx は ASP.NET Core の機能を制限しません。authorization、rate limiting、caching、CORS、custom validation pattern については [保持できるもの](#保持できるもの) を参照してください。

SliceFx は ASP.NET Core の置き換えではありません。Minimal APIs の周囲に、明示的な feature file、生成 contract、portability check を足す vertical-slice layer です。mediator stack や独自 endpoint pipeline を採用せず、Minimal API に近い形を保ちます。

既存 ASP.NET Core app を全面移行する必要はありません。1 endpoint から始め、残りは controller や手書き Minimal API のままにできます。移行時は [Minimal API からの移行](docs/ja/migrations/from-minimal-api.md) を参照してください。

### 最新 benchmark

![Latest source generator benchmark results](https://raw.githubusercontent.com/sano-suguru/slicefx/main/docs/perf/latest.svg)

各 endpoint は static feature file です。source generator はそれらを ASP.NET registration、tooling 用 route metadata、Lambda handler、または handler shape が portable な場合の wasi:http dispatch に変換します。

```bash
dotnet run --project samples/SliceFx.Sample
curl http://localhost:5099/health
```

## Project status

SliceFx は pre-1.0 の experimental software です。API を意図的に安定化するまでは preview package は `0.x` version を使います。

**Release status:** `0.1.0-preview.8` が最新 preview release です。NuGet から install できます。

```bash
dotnet add package SliceFx.Core --version 0.1.0-preview.8
dotnet add package SliceFx.SourceGenerator --version 0.1.0-preview.8
```

repository は .NET 10 を対象にし、`global.json` で SDK `10.0.300` と `rollForward: latestFeature` を指定しています。warning と code-analysis diagnostic は error として扱いますが、通常の PR/main build が新しい SDK analyzer promotion で突然壊れないよう、analyzer recommendation set は `10.0-recommended` に pin しています。

WASI support (`SliceFx.Wasi`) は experimental で、upstream の不安定な toolchain に依存します: [componentize-dotnet](https://github.com/bytecodealliance/componentize-dotnet)、NativeAOT-LLVM preview packages、WASI Preview 2 / `wasi:http@0.2`、Cloudflare Workers 向け JS transpile/shim path。native WASI publish は Linux x64 または Windows x64 が必要で、macOS では Linux x64 Docker cross-build を使います。

全 adapter は opt-in です。必要な package だけ参照してください。ASP.NET-only app では `SliceFx.Core` と `SliceFx.SourceGenerator` だけを使います。

| Package | Purpose |
| --- | --- |
| `SliceFx.Core` | Core runtime: `[Feature]`、`[Filter<T>]`、validation、endpoint filter。 |
| `SliceFx.SourceGenerator` | AOT-friendly な generated registration と route metadata。 |
| `SliceFx.Lambda` | ASP.NET-hosted AWS Lambda adapter。 |
| `SliceFx.Lambda.FunctionPerFeature` | Experimental HTTP API v2 function-per-feature Lambda handler。 |
| `SliceFx.TestHost` | In-process test host helper。 |
| `SliceFx.Wasi` | ASP.NET-independent wasi:http dispatch。 |
| `SliceFx.Wasi.KeyValue` | WASI feature 向け `IKeyValueStore` abstraction と in-memory test double。 |
| `SliceFx.Wasi.HttpClient` | WASI feature 向け outgoing HTTP abstraction と in-memory test double。 |
| `SliceFx.Wasi.Spin` | Spin cron trigger integration 用 abstraction と test double。 |
| `SliceFx.Cli` | scaffolding、route inspection、AWS SAM manifest/package helper、typed client generation。 |

## Hello, SliceFx

`Program.cs`:

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();

var app = builder.Build();

app.MapSlices();
app.Run();
```

`Features/Users/CreateUser.cs`:

```csharp
namespace SliceFx.Sample.Features.Users;

[Feature("POST /users", Summary = "Create a new user")]
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

generator は `[Feature]` class を発見し、`AddSlice()` / `MapSlices()` を生成し、Minimal API binding と validation を接続します。portable endpoint では plain response record を優先してください。`Results.NotFound()` や `Results.NoContent()` のような ASP.NET-specific response helper が必要な場合は `IResult` を使います。

> **DI binding note:** ASP.NET path の binding は通常の Minimal API そのものです。登録済み service は DI から解決されます。concrete service parameter に `[FromServices]` を付ける必要があるのは、handler を ASP.NET、WASI、Lambda の portable dispatch に対応させたい場合です。詳しくは [`docs/ja/guides/parameter-binding.md`](docs/ja/guides/parameter-binding.md) を参照してください。

## Filters and validation

feature filter は標準 ASP.NET Core `IEndpointFilter` です。

```csharp
[Feature("DELETE /users/{id:guid}", Summary = "Delete a user")]
[Filter<RequestLoggingFilter>]
[Filter<RequireApiKeyFilter>]
public static class DeleteUser
{
    public static async Task<IResult> Handle(Guid id, IUserStore store, CancellationToken ct)
    {
        var user = await store.GetAsync(id, ct);
        if (user is null) return Results.NotFound();
        await store.RemoveAsync(id, ct);
        return Results.NoContent();
    }
}
```

`AddSlice()` は参照された filter と matching validator を scoped service として登録し、`[Filter<T>]` は宣言順に filter を適用します。supported DataAnnotations rule は生成されて最初に実行されます。`ISliceValidator<TRequest>` implementation は、`TRequest` が Slice request parameter である場合に自動発見され、feature filter の前に実行されます。

production authorization policy には ASP.NET Core Authorization を優先してください。Slice filter は明示的な per-feature endpoint behavior に向いています。

Read more:

- [Return-type guidance](docs/ja/guides/return-types.md)
- [ASP.NET features and escape hatches](docs/ja/guides/aspnet-features.md)
- [Filter declarations](docs/guides/filter-declarations.md) (English)
- [Filter configuration](docs/patterns/filter-configuration.md) (English)

## 保持できるもの

SliceFx の generated code は pure Minimal API expansion です。ASP.NET の surface はそのまま使えます。

| Need | Default path | Escape hatch |
| --- | --- | --- |
| Validation | request record の DataAnnotations attribute | request type に `ISliceValidator<T>` を実装 |
| Authorization | ASP.NET Core Authorization (`[Authorize]`, policies) | group-level `RequireAuthorization(...)` または fallback policy |
| Rate limiting / caching / CORS | route group: `app.MapGroup("/api").RequireRateLimiting(...).MapSlices()` | per-group または per-endpoint policy |
| Cross-cutting behavior | `[Filter<T>]` endpoint filter | standard ASP.NET Core middleware |

詳しくは [ASP.NET features and escape hatches](docs/ja/guides/aspnet-features.md) を参照してください。

## What works today

| Feature | Status |
| --- | --- |
| `[Feature("METHOD /path")]` declarative routing | Implemented |
| Source-generated `AddSlice()` / `MapSlices()` | Implemented |
| Static handlers with body / route / query / DI / `CancellationToken` binding | Implemented |
| Source-generated DataAnnotations validation | Implemented |
| `ISliceValidator<T>` custom validation | Implemented |
| `[Filter<T>]` endpoint filters | Implemented |
| Route metadata manifest | Experimental |
| `slicefx routes` portability classification | Experimental |
| `slicefx client csharp` typed client generation | Experimental |
| `slicefx client typescript` typed fetch client generation | Experimental |
| AWS SAM manifest generation | Experimental |
| ASP.NET-hosted Lambda adapter | Experimental |
| Function-per-feature Lambda handlers | Experimental HTTP API v2 NativeAOT binary-per-feature packaging |
| TestHost helper | Experimental |
| WASI adapter | Experimental single-component in-process wasi:http dispatch |
| `SliceResult<T>` / `SliceResult` typed WASI results | Implemented |

## Portability

`slicefx routes` は各 feature endpoint を build time に分類します。この情報は typed-client generation、WASI route table、Lambda function-per-feature eligibility に使われます。

| Class | Meaning |
| --- | --- |
| `portable` | plain record または void を返します。typed-client generation、WASI dispatch、function-per-feature Lambda の候補です。 |
| `partial` | handler shape は portable ですが、endpoint filter など一部 behavior が ASP.NET-only です。 |
| `aspnet-only` | `IResult` を返す、または ASP.NET-specific behavior を使います。完全な Minimal API feature set を利用できます。 |

`aspnet-only` は劣っているという意味ではありません。完全な ASP.NET ecosystem を使える標準 Minimal API endpoint です。

## OpenAPI

Slice endpoint は ASP.NET Core の標準 OpenAPI support と連携します。ASP.NET host で `Microsoft.AspNetCore.OpenApi` を追加し、`builder.Services.AddOpenApi()` と `app.MapOpenApi()` を使ってください。

```csharp
builder.Services.AddSlice();
builder.Services.AddOpenApi();

var app = builder.Build();
app.MapSlices();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
```

Slice route manifest は portability classification、client generation、`slicefx openapi` manifest projection 用の build-time artifact です。hosted ASP.NET app では ASP.NET Core OpenAPI document が authoritative です。

## Tooling and adapters

| Topic | Details |
| --- | --- |
| Japanese docs index | [docs/ja/README.md](docs/ja/README.md) |
| Source generator and route manifest | [docs/ja/source-generator.md](docs/ja/source-generator.md) |
| CLI commands | [docs/ja/cli.md](docs/ja/cli.md) |
| Lambda hosting and function-per-feature Lambda | [docs/ja/lambda.md](docs/ja/lambda.md) |
| Minimal API migration | [docs/ja/migrations/from-minimal-api.md](docs/ja/migrations/from-minimal-api.md) |
| ASP.NET features and escape hatches | [docs/ja/guides/aspnet-features.md](docs/ja/guides/aspnet-features.md) |
| Design decisions FAQ | [docs/ja/design-decisions.md](docs/ja/design-decisions.md) |
| Production readiness | [docs/ja/production-readiness.md](docs/ja/production-readiness.md) |

## Build & run

```bash
dotnet build
dotnet run --project samples/SliceFx.Sample
```

Then:

```bash
curl http://localhost:5099/health
curl -X POST http://localhost:5099/users \
  -H "Content-Type: application/json" \
  -d '{"name":"Alice","email":"alice@example.com"}'
curl -X DELETE http://localhost:5099/users/{id} -H "X-API-Key: secret"
```

## License

MIT. See [LICENSE](LICENSE).
