[English](../aot.md)

# NativeAOT — ネイティブバイナリデプロイ

SliceFx のフィーチャーは標準の Minimal API `MapMethods` 登録にコンパイルされます。
ソースジェネレーターがバインディングとシリアライズコードをコンパイル時に生成し、起動時リフレクションを排除しているため、生成コードを .NET NativeAOT でスタンドアローンのネイティブバイナリとして発行できます — JIT なし、runtime-deps バンドルなし、コールドスタートはミリ秒単位です。

## デプロイターゲット比較

| ターゲット | 方法 | 選ぶ場面 |
|---|---|---|
| **素の NativeAOT コンテナ** | `dotnet publish -r <rid>` + distroless イメージ | 汎用軽量サーバー、Azure Container Apps、Fly.io、Kubernetes |
| **Lambda function-per-feature** | `slicefx package aws-lambda --mode function-per-feature` | AWS Lambda でフィーチャー単位のコールドスタート分離([docs/lambda.md](lambda.md)) |
| **WASI コンポーネント** | `dotnet publish -r wasi-wasm` | Cloudflare Workers、Fermyon Spin([samples/SliceFx.WasiSample](../../samples/SliceFx.WasiSample/README.md)) |

素の NativeAOT は最も広くサポートされているパスで、安定した上流ツールチェーンを使います。Lambda function-per-feature と WASI はプレビューパッケージに依存する実験的機能です。

## AOT-safe ディスパッチの有効化

デフォルトでは、SliceFx は `RequestDelegateFactory`(RDF) を通してフィーチャーを登録します。RDF はランタイムリフレクションを使用し、AOT と互換性がありません。`[assembly: SliceAspNetAot]` を追加して生成 AOT-safe 登録モードにオプトインします:

```csharp
// AotSetup.cs
[assembly: SliceAspNetAot]
```

ソースジェネレーターがこの属性を検出し、すべてのパラメータバインディング・バリデーション・JSON シリアライズに `System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>` を使う `new RequestDelegate(…)` ハンドラーを生成します — per-request リフレクションなし。

### JSON コンテキスト

フィーチャーで使うすべてのリクエストボディ型・レスポンス型を含む `JsonSerializerContext` を `[SliceJsonContext(SliceJsonTarget.AspNet)]` で修飾して提供する必要があります:

```csharp
// AotJsonContext.cs
[SliceJsonContext(SliceJsonTarget.AspNet)]
[JsonSerializable(typeof(CreateTodo.Request))]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(List<Todo>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AotJsonContext : JsonSerializerContext { }
```

ジェネレーターは `[JsonSerializable]` のルート集合をコンパイル時のボディパラメータ/DI サービス判別に使います(WASI・Lambda パスと同じ仕組み)。

### 対応バインディングシェイプ

| シェイプ | バインディング |
|---|---|
| ルートパラメータ(`{id}`) | `SliceAotArgumentBinder.TryGetFromRoute<T>` |
| クエリ文字列(`?page=1`) | `SliceAotArgumentBinder.BindFromQuery<T>` |
| ヘッダー | `SliceAotArgumentBinder.BindFromHeader<T>` |
| JSON ボディ | `ReadFromJsonAsync(JsonTypeInfo<T>)` — コンテキストルート要 |
| DI サービス | `RequestServices.GetRequiredService(typeof(T))` |
| キー付きサービス(`[FromKeyedServices]`) | `GetRequiredKeyedService` |
| `CancellationToken` | `HttpContext.RequestAborted` |
| `HttpContext` / `HttpRequest` / `HttpResponse` | 直接パススルー |
| `ClaimsPrincipal` | `HttpContext.User` |

非対応のシェイプ(`IFormFile`、`[AsParameters]`、複数値クエリ、`BindAsync` カスタム型)はビルド時に **SLICE070 エラー** を出します。

### 診断

| ID | 内容 |
|---|---|
| SLICE070 | AOT モードでバインド不能なパラメータ型 |
| SLICE071 | `[SliceJsonContext(AspNet)]` コンテキストに `[JsonSerializable]` ルートが不足 |
| SLICE072 | リフレクション依存 DataAnnotations — 生成対応属性か `ISliceValidator<T>` を使用 |
| SLICE073 | `IResult` 返却: `ExecuteAsync` が直接呼ばれる — AOT 安全性を確認 |
| SLICE074 | 参照 Slice モジュールが `[assembly: SliceAspNetAot]` なしでコンパイルされている |

全診断カタログは [docs/source-generator.md](../source-generator.md) を参照。

**SLICE071 — 型単位の検出。** `[SliceJsonContext(AspNet)]` クラスが存在し、かつ `[JsonSerializable]` エントリが1件以上ある場合、ジェネレーターは必要なルート(レスポンス型・明示 `[FromBody]` パラメータ・規約上のネスト `Request` レコード)がすべて登録されているかを型単位で検査します。不足している型はエラーメッセージに個別に列挙されます。

コンテキストが存在するが `[JsonSerializable]` エントリがゼロの場合、空コンテキストが意図的なものか区別できないため、全ルートが列挙されるコンテキスト欠落扱いのみ発火します。

### 検出スコープと限界

ジェネレーターが自動検出できるルート:

- **レスポンス型** — 全ターゲット。
- **明示 `[FromBody]` パラメータ** — 全ターゲット。
- **ネスト `Request` レコード**(型名がフィーチャークラス名 + `.` で始まる) — POST/PUT/PATCH 規約ボディ; DI コンテナへの登録不要。

コンパイル時に検出できないもの:

- **`[FromBody]` なし・フィーチャークラス非ネストの複合ボディ** — コンパイラが DI サービスと区別できないため検出不能。

### `slicefx json-context`

CLI はジェネレーター診断のワークフローコンパニオンを提供します:

```bash
# 不足エントリを報告(不足あれば非ゼロ終了 — CI ゲートに使用可)
slicefx json-context --check [--target aspnet|wasi|all] [--project path/to/app.csproj]

# 不足エントリをコンテキストファイルにインプレース追記
slicefx json-context --fix [--target aspnet|wasi|all] [--project path/to/app.csproj]
```

`--check` と `--fix` は同時指定可能。フラグを省略した場合は `--check` が暗黙指定されます。

**注意:** ビルド済みプロジェクトではソース生成ルートマニフェストを使用し、未ビルドの場合は `Features/**/*.cs` スキャンにフォールバックします。フォールバック時は戻り値型がソース内の短縮名として現れるため、登録済み FQN エントリはサフィックス一致で照合されます。正確な型単位検出にはプロジェクトを先にビルドしてください。

### バリデーション

ソース生成 DataAnnotations バリデーション(WASI パスと同じルール)が `[Filter<T>]` エンドポイントフィルターの前に実行されます。リフレクション依存のルール(`IValidatableObject`、型レベル属性、`ValidationAttribute` サブクラス)は SLICE072 を生成するので `ISliceValidator<T>` に移してください。

AOT 対応 DataAnnotations 属性: `Required`、`StringLength`、`MinLength`、`MaxLength`、`EmailAddress`、`Url`、`HttpsUrl`、`RegularExpression`、`Range`。

AOT 下で安全な DI パターンについては [docs/guides/aot-safe-scoped-di.md](../guides/aot-safe-scoped-di.md) を参照。

### `AddOpenApi` の注意

`RequestDelegate` ハンドラーを使う `MapMethods` は `RouteHandlerBuilder` ではなく `IEndpointConventionBuilder` を返します。Accepts/Produces メタデータはパラメータ型から推論されません。OpenAPI ドキュメントが必要な場合は `AddOpenApi()` ではなく `slicefx openapi`(マニフェストベース生成)を使ってください。

## 発行手順

### macOS(ホストアーキテクチャのネイティブバイナリを生成)

```bash
dotnet publish samples/SliceFx.AotSample -c Release
# バイナリ: samples/SliceFx.AotSample/bin/Release/net10.0/osx-arm64/publish/SliceFx.AotSample
```

### Linux

```bash
dotnet publish samples/SliceFx.AotSample -c Release -r linux-x64
```

macOS: `xcode-select --install` (Xcode CLT 要)。Linux: `clang` と `zlib1g-dev` が必要:

```bash
sudo apt-get install clang zlib1g-dev
```

**バイナリサイズ**(Release、Strip済み): フィーチャー数や `InvariantGlobalization=true` の有無によって約 12〜20 MB。

## コンテナ

サンプルには multi-stage `Dockerfile` が同梱されています:

1. **Build ステージ**(`mcr.microsoft.com/dotnet/sdk:10.0`) — `clang` + `zlib1g-dev` をインストール(SDK イメージに非同梱)して `dotnet publish`。Apple Silicon から x64 バイナリを生成するには `--platform linux/amd64` を付ける。
2. **Runtime ステージ**(`mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled`) — 約 13 MB の minimal Ubuntu Noble distroless イメージ(デフォルト非 root 実行)。

```bash
# リポジトリルートからビルド
docker build -f samples/SliceFx.AotSample/Dockerfile -t slicefx-aot .

# Apple Silicon から x64 強制
docker build --platform linux/amd64 \
  -f samples/SliceFx.AotSample/Dockerfile \
  -t slicefx-aot .

# 実行
docker run --rm -p 8080:8080 slicefx-aot

# 確認
curl http://localhost:8080/health
```

**コンテナイメージ全体のサイズ**: 約 25〜35 MB。

## CI 検証

`.github/workflows/ci.yml` の `nativeaot-sample` ジョブがプッシュ・PR のたびに実行されます:

1. `dotnet publish samples/SliceFx.AotSample -c Release -r linux-x64` — `TreatWarningsAsErrors=true` のため、`IL2026`/`IL3050` 診断があるとこのステップが失敗し、生成ディスパッチのリフレクションフリーを継続的に担保します。
2. スモークテスト: `GET /health`(200)、`POST /todos`(200)、バリデーション 400、`SliceResult<T>` 404。
3. バイナリサイズをステップサマリに出力。

`tests/SliceFx.AotSample.Tests/` の TestHost テストは JIT 下で AOT モード生成コードを実行してハンドラーロジックを検証し、メインテストスイートの一部として実行されます。発行ステップが trim/AOT 固有の問題を捕まえる唯一のゲートです。

## 制約

- **Accepts/Produces 推論なし**: `RequestDelegate` 登録エンドポイントはパラメータ型ベースのメタデータを生成しません。正確な OpenAPI ドキュメントには `slicefx openapi` を使用。
- **グループレベルフィルター**: `MapGroup(…).AddEndpointFilter(…)` は `RequestDelegate` を空 `Arguments` でラップします。フィーチャーに直接宣言した `[SliceFilter<T>]` と `[Filter<T>]` は正しく合成されます。
- **ボディ/DI 判別**: `[JsonSerializable]` ルート集合によりコンパイル時に決定されます。DI コンテナとコンテキスト両方に登録されている型は AOT モードではボディからバインドされます。
