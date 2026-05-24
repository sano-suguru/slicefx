# Slice production readiness criteria

This document defines the **objective gate values** for deciding whether Slice is ready to adopt in production. These are readiness targets, not a claim that Slice is production-ready today.

Current public status: `0.1.0-preview.1` is unreleased, the packages are not on NuGet yet, and no production adoption is claimed.

## Strength-preservation invariants

Before discussing gate values, the six differentiators that must **never** be eroded by any change. Every phase and task passes through this filter before being adopted.

1. **100% pure ASP.NET Core Minimal API expansion** — generated code only chains standard APIs.
2. **`Slice.Core` is dependency-free** (`FrameworkReference` only).
3. **No new startup-time reflection** — Native AOT friendliness preserved.
4. **No implicit magic** — filters that are not declared in source are never injected.
5. **Convention violations surface at compile time** — through the type system or analyzer diagnostics, not at runtime.
6. **`slice routes` / `slice client csharp` tooling stays uninterrupted** — the route manifest schema is not broken.

## Baseline measurements

Measured on Apple M1 (8 cores, macOS 26.4.1, .NET SDK 10.0.300, BenchmarkDotNet 0.14.0) on 2026-05-23. CI hardware (Ubuntu / x64) will differ; re-run on the perf workflow's host to confirm.

### Source generator throughput

| Method                         | FeatureCount | Mean     | Allocated |
|-------------------------------- |------------- |---------:|----------:|
| ColdRun                         | 50           | 2.78 ms | 2.89 MB |
| WarmRun_NoOpEdit                | 50           | 2.62 ms | 2.19 MB |
| WarmRun_TrackedTreeTrivialEdit  | 50           | 2.67 ms | 2.20 MB |
| CompilationEditOnly             | 50           | 0.001 ms | 0.002 MB |
| ColdRun                         | 100          | 3.22 ms | 3.67 MB |
| WarmRun_NoOpEdit                | 100          | 2.84 ms | 2.29 MB |
| WarmRun_TrackedTreeTrivialEdit  | 100          | 3.03 ms | 2.29 MB |
| CompilationEditOnly             | 100          | 0.001 ms | 0.002 MB |
| ColdRun                         | 200          | 3.63 ms | 5.14 MB |
| WarmRun_NoOpEdit                | 200          | 2.98 ms | 2.43 MB |
| WarmRun_TrackedTreeTrivialEdit  | 200          | 3.08 ms | 2.44 MB |
| CompilationEditOnly             | 200          | 0.001 ms | 0.002 MB |

The table above is the local Apple M1 baseline. The chart below is generated from BenchmarkDotNet JSON and uses `tests/Slice.Benchmarks/gates.json` for the dotted gate lines; after the nightly perf workflow runs on `main`, it reflects the latest GitHub Actions Ubuntu x64 measurement. The SVG caption identifies the actual measurement host.

![Latest GitHub Actions source generator benchmark chart](perf/latest.svg)

Reproduce with `dotnet run -c Release --project tests/Slice.Benchmarks --no-build -- --filter "*"`.

### Gate values (derived from baseline)

Gates are set at roughly 2× baseline to leave headroom for noisier CI hardware; the 100-feature tracked-tree warm gate includes a small CI variance buffer based on the Ubuntu x64 perf run. The single source of truth is `tests/Slice.Benchmarks/gates.json`; the nightly `Perf` workflow (`.github/workflows/perf.yml`) parses BenchmarkDotNet JSON output and runs `tests/Slice.Benchmarks/check-gates.sh`, failing the workflow if any gate is breached. Edit `gates.json` and this table together.

| Metric | Gate | Baseline (Apple M1) | How to measure |
|---|---|---|---|
| Source generator cold run (100 features) | < 8 ms | 3.22 ms | `SourceGeneratorBenchmarks.ColdRun` Mean |
| No-op edit re-run (100 features) | < 6 ms | 2.84 ms | `SourceGeneratorBenchmarks.WarmRun_NoOpEdit` Mean |
| Tracked-tree trivial edit re-run (100 features) | < 7 ms | 3.03 ms | `SourceGeneratorBenchmarks.WarmRun_TrackedTreeTrivialEdit` Mean |
| Source generator cold run (200 features) | < 10 ms | 3.63 ms | `SourceGeneratorBenchmarks.ColdRun` Mean |
| No-op edit re-run (200 features) | < 8 ms | 2.98 ms | `SourceGeneratorBenchmarks.WarmRun_NoOpEdit` Mean |
| Tracked-tree trivial edit re-run (200 features) | < 8 ms | 3.08 ms | `SourceGeneratorBenchmarks.WarmRun_TrackedTreeTrivialEdit` Mean |
| Allocations per cold generator pass (200 features) | < 8 MB | 5.03 MB | `MemoryDiagnoser` Allocated |
| Tracked-step cache reuse on no-op and trivial edits | 100% (Cached/Unchanged) | Verified | `IncrementalCacheTests` (`SliceFeatureModels`, `SliceReferencedModules`, `SliceEmitPlan`) |
| `Slice.Core.dll` size | < 50 KB | (measure during release) | `bin/Release/net10.0/Slice.Core.dll` |

### Observations from the baseline

1. **Absolute numbers are very small** — even at 200 features the cold run is under 4 ms on M1. This is well below any threshold at which IDE responsiveness would be a concern.
2. **WarmRun is now faster than ColdRun on the generator path**. `WarmRun_NoOpEdit` precomputes the edited compilation and measures the generator re-run only; `CompilationEditOnly` isolates the sub-microsecond syntax-tree replacement cost. The final `SliceEmitPlan` step is cacheable, so `RegisterSourceOutput` only reports diagnostics and adds cached source text when inputs are unchanged.
3. **Tracked feature-tree trivial edits are covered**. `WarmRun_TrackedTreeTrivialEdit` edits implementation trivia inside the feature syntax tree and stays below the 200-feature cold run, proving the optimization is not limited to unrelated-file edits.
4. **The emit plan trades a small amount of steady-state driver memory for lower warm-run work** by retaining generated source strings and structural diagnostic data in the incremental state table. At 200 features this still allocates less than half of a cold run.

### MapSlices() startup cost

Not yet measured in `Slice.Benchmarks`. Future candidate. Today, the manual proxy is: `dotnet run --project samples/Slice.Sample` followed by `curl http://localhost:5099/health` — startup is sub-second on a development laptop.

## Verification flow

1. **Incremental cache tests**: `dotnet test tests/Slice.SourceGenerator.Tests --filter "FullyQualifiedName~IncrementalCacheTests"` is green.
2. **Run benchmarks locally**: `dotnet run -c Release --project tests/Slice.Benchmarks -- --filter "*"` produces numbers for 50 / 100 / 200 features. Output lands in `BenchmarkDotNet.Artifacts/results/*-report-full.json`.
3. **Check gates locally**: `bash tests/Slice.Benchmarks/check-gates.sh tests/Slice.Benchmarks/gates.json BenchmarkDotNet.Artifacts/results` exits non-zero if any gate in `gates.json` is breached. The nightly `Perf` workflow runs this same script.
4. **Adjust baseline**: when intentionally tightening or loosening gates, edit `gates.json` and the table above together so they stay in sync.
5. **Strength-preservation regression check**:
   - Before and after a refactor, `diff` of `obj/Generated/Slice.SourceGenerator/**/*.g.cs` is empty.
   - `slice routes --format json` output is unchanged.
   - `Slice.Core` has zero `<PackageReference>` entries (the MSBuild `ValidateSliceCorePackageReferences` target enforces this automatically).

## Adoption evidence

| Evidence type | Current public count | Notes |
|---|---:|---|
| Production adoption | 0 | Do not claim production readiness from repository checks alone. |
| Published personal dogfooding logs | 0 | Add a maintainer side-project write-up before claiming real-world usage. |

## Adoption matrix

These recommendations apply to experiments, pilots, and preview evaluation. Production use requires a published package, completed release smoke tests, project-specific acceptance criteria, external production references, and an explicit API-stability commitment.

| Project shape | Recommendation |
|---|---|
| Serverless / WASI microservices | **Preview candidate** — evaluate portability rules and unstable upstream WASI tooling first |
| Full-stack C# (Blazor / .NET client) with typed-client needs | **Preview candidate** — evaluate generated clients against the actual route set |
| Small-to-medium web API (< 50 endpoints) | **Acceptable for experiments** |
| Large internal monolith (> 200 endpoints, complex domain) | **PoC required** — decide after measuring with phase 0 benchmarks |
| Existing MediatR / IPipelineBehavior-driven projects | **Not a fit** — incompatible philosophies |

## Known constraints (beyond gate values)

- DataAnnotations attributes such as `Range`, `RegularExpression`, and `EmailAddress` fall back to reflection on the **WASI path**. The ASP.NET path was always reflection-based, so this is invisible there.
- WASI deployment depends on preview upstream build/transpile tools. Spin consumes the generated `wasi:http` component directly; Cloudflare Workers adds `jco`, `preview2-shim`, Wrangler, and a compatibility-date-sensitive JS shim.
- `[Filter<T>]` takes a type parameter only (configuration arrives through constructor DI). This is an intentional constraint that protects strength-preservation principle #1. See `docs/patterns/filter-configuration.md`.
