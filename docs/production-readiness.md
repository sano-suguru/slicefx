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

Measured on Apple M1 (8 cores, macOS 26.4.1, .NET SDK 10.0.300, BenchmarkDotNet 0.14.0) on 2026-05-21. CI hardware (Ubuntu / x64) will differ; re-run on the perf workflow's host to confirm.

### Source generator throughput

| Method           | FeatureCount | Mean     | Allocated |
|----------------- |------------- |---------:|----------:|
| ColdRun          | 50           | 3.86 ms | 3.02 MB |
| WarmRun_NoOpEdit | 50           | 4.45 ms | 2.79 MB |
| ColdRun          | 100          | 5.19 ms | 3.88 MB |
| WarmRun_NoOpEdit | 100          | 5.96 ms | 3.41 MB |
| ColdRun          | 200          | 6.73 ms | 5.57 MB |
| WarmRun_NoOpEdit | 200          | 9.08 ms | 4.68 MB |

The table above is the local Apple M1 baseline. The chart below is generated from BenchmarkDotNet JSON and uses `tests/Slice.Benchmarks/gates.json` for the dotted gate lines; after the nightly perf workflow runs on `main`, it reflects the latest GitHub Actions Ubuntu x64 measurement. The SVG caption identifies the actual measurement host.

![Latest GitHub Actions source generator benchmark chart](perf/latest.svg)

Reproduce with `dotnet run -c Release --project tests/Slice.Benchmarks --no-build -- --filter "*"`.

### Gate values (derived from baseline)

Gates are set at roughly 2× baseline to leave headroom for noisier CI hardware. The single source of truth is `tests/Slice.Benchmarks/gates.json`; the nightly `Perf` workflow (`.github/workflows/perf.yml`) parses BenchmarkDotNet JSON output and runs `tests/Slice.Benchmarks/check-gates.sh`, failing the workflow if any gate is breached. Edit `gates.json` and this table together.

| Metric | Gate | Baseline (Apple M1) | How to measure |
|---|---|---|---|
| Source generator cold run (100 features) | < 12 ms | 5.19 ms | `SourceGeneratorBenchmarks.ColdRun` Mean |
| No-op edit re-run (100 features) | < 12 ms | 5.96 ms | `SourceGeneratorBenchmarks.WarmRun_NoOpEdit` Mean |
| Source generator cold run (200 features) | < 15 ms | 6.73 ms | `SourceGeneratorBenchmarks.ColdRun` Mean |
| No-op edit re-run (200 features) | < 20 ms | 9.08 ms | `SourceGeneratorBenchmarks.WarmRun_NoOpEdit` Mean |
| Allocations per generator pass (200 features) | < 8 MB | 5.57 MB | `MemoryDiagnoser` Allocated |
| Tracked-step cache reuse on a no-op edit | 100% (Cached/Unchanged) | Verified | `IncrementalCacheTests` (`SliceFeatureModels`, `SliceReferencedModules`) |
| `Slice.Core.dll` size | < 50 KB | (measure during release) | `bin/Release/net10.0/Slice.Core.dll` |

### Observations from the baseline

1. **Absolute numbers are very small** — even at 200 features the cold run is under 7 ms on M1. This is well below any threshold at which IDE responsiveness would be a concern.
2. **WarmRun is *slower* than ColdRun in wall-clock time, despite allocating less** (e.g., 200 features: 9.08 ms vs 6.73 ms; 4.68 MB vs 5.57 MB). The upstream `SliceFeatureModels` and `SliceReferencedModules` steps are confirmed cached by `IncrementalCacheTests`, so this delta comes from the `RegisterSourceOutput` action body re-running emit work. Output text is identical (test verifies this), but the emit step is not free.
   - Follow-up candidate: gate the emit body itself behind a Select that produces a single cacheable record, so the SourceOutput action becomes a no-op when inputs are unchanged. Not blocking — the absolute cost is tiny.
3. **GC pressure scales linearly** with feature count. Gen2 collections appear only at 100+ features. Manageable.

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
| Serverless / WASI microservices | **Preview candidate** — evaluate portability rules and upstream WASI tooling first |
| Full-stack C# (Blazor / .NET client) with typed-client needs | **Preview candidate** — evaluate generated clients against the actual route set |
| Small-to-medium web API (< 50 endpoints) | **Acceptable for experiments** |
| Large internal monolith (> 200 endpoints, complex domain) | **PoC required** — decide after measuring with phase 0 benchmarks |
| Existing MediatR / IPipelineBehavior-driven projects | **Not a fit** — incompatible philosophies |

## Known constraints (beyond gate values)

- DataAnnotations attributes such as `Range`, `RegularExpression`, and `EmailAddress` fall back to reflection on the **WASI path**. The ASP.NET path was always reflection-based, so this is invisible there.
- `[Filter<T>]` takes a type parameter only (configuration arrives through constructor DI). This is an intentional constraint that protects strength-preservation principle #1. See `docs/patterns/filter-configuration.md`.
