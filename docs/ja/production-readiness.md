# SliceFx production readiness criteria

[English](../production-readiness.md) | [日本語 docs index](README.md)

> この日本語版は参考訳です。production readiness の判断は英語版を正本とします。

この文書は、SliceFx を production adoption してよいか判断するための **objective gate values** を定義します。これは readiness target であり、SliceFx が現時点で production-ready であるという主張ではありません。

Current public status: `0.1.0-preview.8` is published on NuGet (2026-06-01)。production adoption はまだ claim しません。

## Strength-preservation invariants

gate value の前に、どの変更でも **絶対に損なってはいけない** 6つの differentiator を確認します。すべての phase / task は採用前にこの filter を通ります。

1. **100% pure ASP.NET Core Minimal API expansion** — generated code は standard API だけを chain します。
2. **`SliceFx.Core` is dependency-free** (`FrameworkReference` only)。
3. **No new startup-time reflection** — Native AOT friendliness を守ります。
4. **No implicit magic** — source に宣言されていない filter は注入しません。
5. **Convention violations surface at compile time** — type system または analyzer diagnostic で出し、runtime に持ち込みません。
6. **`slicefx routes` / `slicefx client csharp` tooling stays uninterrupted** — route manifest schema を壊しません。

## Baseline measurements

Apple M1 (8 cores, macOS 26.4.1, .NET SDK 10.0.300, BenchmarkDotNet 0.14.0) で 2026-05-23 に測定した baseline です。CI hardware (Ubuntu / x64) では異なるため、perf workflow の host で再確認します。

### Source generator throughput

| Method | FeatureCount | Mean | Allocated |
| --- | ---: | ---: | ---: |
| ColdRun | 50 | 2.78 ms | 2.89 MB |
| WarmRun_NoOpEdit | 50 | 2.62 ms | 2.19 MB |
| WarmRun_TrackedTreeTrivialEdit | 50 | 2.67 ms | 2.20 MB |
| CompilationEditOnly | 50 | 0.001 ms | 0.002 MB |
| ColdRun | 100 | 3.22 ms | 3.67 MB |
| WarmRun_NoOpEdit | 100 | 2.84 ms | 2.29 MB |
| WarmRun_TrackedTreeTrivialEdit | 100 | 3.03 ms | 2.29 MB |
| CompilationEditOnly | 100 | 0.001 ms | 0.002 MB |
| ColdRun | 200 | 3.63 ms | 5.14 MB |
| WarmRun_NoOpEdit | 200 | 2.98 ms | 2.43 MB |
| WarmRun_TrackedTreeTrivialEdit | 200 | 3.08 ms | 2.44 MB |
| CompilationEditOnly | 200 | 0.001 ms | 0.002 MB |

下の chart は BenchmarkDotNet JSON と `tests/SliceFx.Benchmarks/gates.json` から生成されます。nightly perf workflow が `main` で走った後は、最新の GitHub Actions Ubuntu x64 measurement を反映します。

![Latest GitHub Actions source generator benchmark chart](../perf/latest.svg)

再現:

```bash
dotnet run -c Release --project tests/SliceFx.Benchmarks --no-build -- --filter "*"
```

### Gate values

gate は CI hardware の noise を吸収するため baseline のおおむね 2x に設定します。single source of truth は `tests/SliceFx.Benchmarks/gates.json` です。nightly `Perf` workflow (`.github/workflows/perf.yml`) は BenchmarkDotNet JSON output を parse し、`tests/SliceFx.Benchmarks/check-gates.sh` で gate breach を検出します。

| Metric | Gate | Baseline (Apple M1) |
| --- | --- | --- |
| Source generator cold run (100 features) | < 8 ms | 3.22 ms |
| No-op edit re-run (100 features) | < 6 ms | 2.84 ms |
| Tracked-tree trivial edit re-run (100 features) | < 7 ms | 3.03 ms |
| Source generator cold run (200 features) | < 10 ms | 3.63 ms |
| No-op edit re-run (200 features) | < 8 ms | 2.98 ms |
| Tracked-tree trivial edit re-run (200 features) | < 8 ms | 3.08 ms |
| Allocations per cold generator pass (200 features) | < 8 MB | 5.03 MB |
| Tracked-step cache reuse | 100% | Verified |
| `SliceFx.Core.dll` size | < 50 KB | release 時に測定 |
| Startup / runtime request gates | 英語版の table と gate file を参照 | `runtime-gates.json` |

source generator gate は `tests/SliceFx.Benchmarks/gates.json`、runtime gate は `tests/SliceFx.Benchmarks.Runtime/runtime-gates.json` が source of truth です。table と gate file は一緒に更新します。

### Observations from the baseline

1. **absolute number は小さい** — 200 features でも cold run は M1 で 4 ms 未満です。
2. **WarmRun は generator path で ColdRun より速い** — final `SliceEmitPlan` step が cacheable で、`RegisterSourceOutput` は diagnostic 報告と cached source text 追加のみ行います。
3. **tracked feature-tree trivial edit も covered** — feature syntax tree 内の implementation trivia edit でも 200-feature cold run 未満に収まります。
4. **emit plan は少量の steady-state driver memory と引き換えに warm-run work を減らします**。

### MapSlices() startup cost and runtime request handling

2026-05-27 に Apple M1 / macOS 26.4.1 / .NET SDK 10.0.300 / BenchmarkDotNet 0.14.0 / ServerGC enabled で測定しました。`Startup` は warm in-process host build cost (CreateSlimBuilder + DI container build + MapSlices + forced endpoint materialization) です。process cold-start time ではありません。

| Method | FeatureCount | Mean | Allocated |
| --- | ---: | ---: | ---: |
| BuildHostOnly | 50 | 77.4 ms | 0.30 MB |
| BuildHostOnly | 100 | 79.3 ms | 0.30 MB |
| BuildHostOnly | 200 | 78.5 ms | 0.30 MB |
| Startup | 50 | 161.7 ms | 1.45 MB |
| Startup | 100 | 232.1 ms | 2.64 MB |
| Startup | 200 | 355.7 ms | 5.93 MB |
| Request_SimpleGet | 50 | 0.149 ms | 0.011 MB |
| Request_SimpleGet | 100 | 0.152 ms | 0.011 MB |
| Request_SimpleGet | 200 | 0.176 ms | 0.011 MB |
| Request_PostWithValidation | 50 | 0.189 ms | 0.014 MB |
| Request_PostWithValidation | 100 | 0.178 ms | 0.014 MB |
| Request_PostWithValidation | 200 | 0.185 ms | 0.014 MB |

key observation:

- `BuildHostOnly` は feature count に依存しません。DI container `Build()` が固定 host-init floor です。
- `Startup - BuildHostOnly` が SliceFx MapSlices + RequestDelegateFactory cost で、feature count に対してほぼ linear に増えます。
- per-request latency は feature count に依存しません。endpoint table build 後の routing は O(log n) で、hot-path dispatch は reflection-free です。

再現:

```bash
dotnet run -c Release --project tests/SliceFx.Benchmarks.Runtime --no-build -- --filter '*' --artifacts BenchmarkDotNet.Artifacts.Runtime
```

## Verification flow

1. **Incremental cache tests**: `dotnet test tests/SliceFx.SourceGenerator.Tests --filter "FullyQualifiedName~IncrementalCacheTests"` が green。
2. **source generator benchmark を local 実行**: `dotnet run -c Release --project tests/SliceFx.Benchmarks -- --filter "*"`。
3. **source generator gate を local check**: `bash tests/SliceFx.Benchmarks/check-gates.sh tests/SliceFx.Benchmarks/gates.json BenchmarkDotNet.Artifacts/results`。
4. **runtime benchmark を local 実行**: `dotnet run -c Release --project tests/SliceFx.Benchmarks.Runtime -- --filter "*" --artifacts BenchmarkDotNet.Artifacts.Runtime`。
5. **runtime gate を local check**: `bash tests/SliceFx.Benchmarks/check-gates.sh tests/SliceFx.Benchmarks.Runtime/runtime-gates.json BenchmarkDotNet.Artifacts.Runtime/results`。
6. **baseline adjustment**: gate を意図的に変える場合は `gates.json` / `runtime-gates.json` と table を一緒に更新します。
7. **Strength-preservation regression check**:
   - refactor 前後で `obj/Generated/SliceFx.SourceGenerator/**/*.g.cs` の diff が空。
   - `slicefx routes --format json` output が unchanged。
   - `SliceFx.Core` に `<PackageReference>` entry がない。

## Adoption evidence

| Evidence type | Current public count | Notes |
| --- | ---: | --- |
| Production adoption | 0 | repository check だけで production readiness を claim しません。 |
| Published personal dogfooding logs | 0 | real-world usage を claim する前に maintainer side-project write-up を追加します。 |

## Adoption matrix

これらの recommendation は experiment、pilot、preview evaluation 向けです。production use には published package、release smoke test、project-specific acceptance criteria、external production references、explicit API-stability commitment が必要です。

| Project shape | Recommendation |
| --- | --- |
| Serverless / WASI microservices | **Preview candidate** — portability rule と unstable upstream WASI tooling を先に評価します。 |
| Full-stack C# (Blazor / .NET client) with typed-client needs | **Preview candidate** — generated client を実 route set で評価します。 |
| New or already feature-shaped small-to-medium web API (< 50 endpoints) | **Acceptable for experiments** |
| Existing ASP.NET Core Minimal API or MVC API considering SliceFx | **Migration PoC recommended** — low-risk endpoint を1つ移行し、migration audit と API contract 比較を行います。 |
| Large internal monolith (> 200 endpoints, complex domain) | **PoC required** — phase 0 benchmark を測定して判断します。 |
| Existing MediatR / IPipelineBehavior-driven projects | **Not a fit** — philosophy が合いません。 |

## Known constraints

- supported DataAnnotations attribute は ASP.NET、Lambda function-per-feature、WASI path で source-generated されます。custom `ValidationAttribute`、type-level validation、`IValidatableObject`、resource-based message などの reflection-bound validation は build time に報告され、portable dispatch から除外されます。
- WASI deployment は preview upstream build/transpile tool に依存します。Spin は generated `wasi:http` component を直接 consume します。Cloudflare Workers は `jco`、`preview2-shim`、Wrangler、compatibility-date-sensitive JS shim を追加します。
- WASI per-feature packaging は実装されていません。supported WASI deployment shape は、generated route table を含む1つの component が eligible route を in-process dispatch する形です。
- `[Filter<T>]` は type parameter のみを取ります。configuration は constructor DI で渡します。これは strength-preservation principle #1 を守るための意図的な制約です。
