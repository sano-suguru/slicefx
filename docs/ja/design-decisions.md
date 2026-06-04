# Design decisions FAQ

[English](../design-decisions.md) | [日本語 docs index](README.md)

> この日本語版は参考訳です。仕様判断は英語版を正本とします。

このページは SliceFx でよく出る設計上の質問をまとめた FAQ です。各判断は [production-readiness.md](production-readiness.md) にある strength-preservation invariants を守るためのものです。

## なぜ `IMediator` / `IPipelineBehavior` layer を持たないのか

SliceFx は ASP.NET Core の上に mediator stack を追加しません。

- ASP.NET Core の `IEndpointFilter` は cross-cutting concern を native support、scoped DI、OpenAPI integration 付きで扱えます。2つ目の pipeline を足すのは重複です。
- mediator は per-feature dispatch path を reflection や container resolution の背後に隠します。SliceFx の source generator は `endpoints.MapMethods(pattern, [method], delegate)` を直接 emit するため、AOT-friendly で stack trace も短く保てます。
- SliceFx は Minimal API に近い形を保ちたい project を対象にしています。MediatR / `IPipelineBehavior` に強く投資済みの project は主対象ではありません。

filter 的な behavior が必要なら `[Filter<T>]` を付けます。validation には supported DataAnnotations attribute または `ISliceValidator<T>` を使います。

## なぜ `WebApplication.CreateSlimBuilder` なのか

`CreateSlimBuilder` は trimming-friendly な host です。API-only app に不要な Razor / MVC service を既定で入れないため、trimmed / AOT-published binary を小さくできます。SliceFx は Lambda / WASI のように binary size が重要な環境への portability も価値に含めるため、sample ではこの builder を使います。省略された service が必要な project では `CreateBuilder` に切り替えて構いません。

## なぜ `SliceFx.Core` は zero-dependency なのか

runtime surface area を監査しやすくし、supply-chain creep を避けるためです。許可される参照は `<FrameworkReference Include="Microsoft.AspNetCore.App" />` だけです。

この制約は2か所で冗長に守られます。

1. **Local build**: `Directory.Build.targets` の `ValidateSliceCorePackageReferences` MSBuild target が、`src/SliceFx.Core/SliceFx.Core.csproj` に `<PackageReference>` が追加された場合に build を失敗させます。
2. **CI**: `.github/workflows/ci.yml` の PowerShell step が project file を再確認します。

satellite package (`SliceFx.SourceGenerator`, `SliceFx.Lambda`, `SliceFx.TestHost`, `SliceFx.Wasi`, `SliceFx.Cli`) は NuGet dependency を持てます。制約されるのは `SliceFx.Core` だけです。

## なぜ startup reflection ではなく source generator なのか

理由は3つです。

1. **generated route discovery / dispatch / validation が reflection を避けるため。** request time に動くものは reflection-free である必要があります。
2. **convention violation を compile time に出すため。** generator は missing `Handle`、ambiguous overload、non-public handler、invalid filter type、unsupported WASI body binding、AOT metadata 不足、duplicate endpoint name などを SLICE diagnostic として出します。
3. **tooling reuse のため。** 同じ generator が route manifest (`{Asm}_SliceRouteManifest.g.cs`) を emit し、`slicefx routes` や `slicefx client csharp` が使います。

implementation は `src/SliceFx.SourceGenerator/` にあります。

## なぜ Feature class と `Handle` method は static なのか

**`Handle` は `public static` 必須です。** source generator は handler を method-group delegate として emit します。

```csharp
// generated output (RegistrationEmitter.cs)
app.MapMethods(
    "/users",
    new[] { "POST" },
    new Func<Request, IUserStore, CancellationToken, Task<Response>>(
        CreateUser.Handle))
```

static method group は instance capture、per-request allocation、boxing を避けて delegate に変換できます。trimmer も delegate から method への参照を静的に追跡できます。`Handle` がない場合は SLICE001、`public static` でない場合は SLICE002、複数ある場合は SLICE003 です。

**containing class も `public static` が推奨です。** generator が強制するわけではありませんが、feature が instance state を持たないことを明示し、constructor や inheritance の誤用を防ぎます。

## なぜ `IIncrementalGenerator` なのか

Roslyn の `IIncrementalGenerator` は、pipeline が cacheable input で構成されている場合にだけ keystroke ごとの再実行を避けられます。SliceFx は各 stage に `WithTrackingName` を付け、`Compilation` を小さな equatable record に減らし、`IncrementalCacheTests` で cache behavior を検証しています。

perf baseline と gate は [production-readiness.md](production-readiness.md) にあります。

## なぜ DataAnnotations と `ISliceValidator<T>` の両方があるのか

`Required`、`MinLength`、`EmailAddress` などの declarative rule は request record の primary constructor で読みやすく、generated validation として最初に実行できます。

cross-field rule、async check、custom validation attribute、DI が必要な rule は generated attribute だけでは表現しにくいため、`ISliceValidator<TRequest>` を使います。generator は validator を登録し、generated DataAnnotations の後、user-declared `[Filter<T>]` の前に実行します。

どちらも `SliceFx.Core` にあり、追加 NuGet package は不要です。

## なぜ `[Filter<T>]` は type parameter だけで constructor args を持たないのか

この制約は「100% pure ASP.NET Core Minimal API expansion」を守るためです。attribute に instance state を持たせると、generator が値を singleton に持ち上げるか closure を emit する必要があり、標準 API の単純な chain ではなくなります。

filter は scoped service です。設定は constructor DI、`IOptions<T>`、または任意の service から渡します。同じ filter logic を複数 policy で使う場合は `[Filter<AuditFilter<AdminAuditPolicy>>]` のような closed generic filter を使えます。詳細は [filter-configuration.md](../patterns/filter-configuration.md) (English) を参照してください。

## なぜ一部 feature は `aspnet-only` になり WASI から除外されるのか

WASI exclusion と route-manifest portability は同じ vocabulary を使いますが、check layer は別です。

- **Route manifest portability**: feature が `IResult` / `Task<IResult>` を返す場合は `aspnet-only`、reflection-bound DataAnnotations validation や endpoint filter によって完全な WASI behavior が妨げられる場合は `partial` です。
- **WASI route table emission**: JSON body/response route には `[SliceJsonContext(SliceJsonTarget.Wasi)]` 付き source-generated `JsonSerializerContext` が必要です。ない場合は SLICE021 が出て `WasiRouteTable` から除外されます。

manifest classification は `slicefx routes` と client generation に使われ、WASI generator path は独自の route-table eligibility check を行います。

## なぜ "generate everything" CLI flag ではなく opt-in adapter なのか

各 satellite package は独自の NuGet dependency を持ちます。全 consumer に強制すると `SliceFx.Core` の zero-dep value が弱くなり、AOT publisher が望まない transitive package も入ります。package reference で opt-in することで dependency graph を明示します。

`SliceFx.Wasi` は experimental で、publish も preview upstream tooling に依存します。opt-in satellite にしておくことで、その toolchain risk を ASP.NET-only app に押し付けません。

## warm-run を cold-run より速く保つには

`SliceFeatureModels`、`SliceReferencedModules`、`SliceEmitPlan` などの upstream stage を cacheable にし、`RegisterSourceOutput` は structural diagnostic の報告と cached source text の追加だけを行います。benchmark suite は no-op edit と tracked-tree trivial edit を分け、generator reuse を測定します。

## なぜ `slicefx` CLI を .NET local tool として出すのか

local tool は repository ごとに version pin できます。`dotnet tool restore` 後の `slicefx routes` がどの machine でも同じ version で動きます。global tool は drift しやすく、project executable は tool version を build output と混ぜてしまいます。

## なぜ function-per-feature artifact は feature ごとに process と DI container を持つのか

理由は3つです。

**Blast-radius isolation.** `CreateOrder` の bug、memory leak、misconfigured singleton が `GetOrder` に影響しません。

**NativeAOT trimming correctness.** per-feature wrapper project は trimmer に feature-scoped root を与えます。sibling feature entrypoint や app-wide registration surface が root されると trimming guarantee が崩れるため、`slicefx package` が closure inspection で検出します。

**Independent DI singleton state.** `ILambdaFunctionPerFeatureStartup.ConfigureServices` は cold-start 時に process ごとに呼ばれます。singleton lifetime は feature ごとの traffic pattern に合わせて分離されます。

cross-feature coordination が必要な場合は external store を使います。shared DI container が必要なら hosted Lambda mode (`SliceFx.Lambda`) を使います。詳細は [lambda.md](lambda.md#per-feature-isolation) を参照してください。
