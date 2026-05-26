# OSS pre-release readiness checklist

SliceFx is pre-1.0 experimental software. Use `0.x` preview versions until the public API is intentionally stabilized.

This checklist is for deciding whether the repository is ready for a preview release. It intentionally separates pre-release preparation from the final publish actions.

Current Go/No-Go: **No-Go** until local verification, smoke tests, release notes, and NuGet publish verification are complete.

## Repository readiness

- [x] Add an MIT `LICENSE` file.
- [x] Add contribution, security, code of conduct, and changelog documents.
- [x] Keep CI build and formatter checks in `.github/workflows/ci.yml`.
- [x] Keep the `SliceFx.Core` runtime package dependency-free except for `Microsoft.AspNetCore.App`.
- [x] Add package metadata for NuGet preview packages.
- [x] Add a GitHub Pages landing page under `docs/`.

## Pre-release gates

- [x] Confirm the core NuGet package identity before publishing anything.
  - `Slice` already exists on NuGet and is associated with an older unrelated package.
  - NuGet exact lookup showed `SliceFx`, `SliceFx.Core`, `SliceFx.SourceGenerator`, `SliceFx.Lambda`, `SliceFx.Lambda.FunctionPerFeature`, `SliceFx.TestHost`, `SliceFx.Wasi`, and `SliceFx.Cli` are unused before the first preview publish.
  - NuGet search noise check: `SliceFx.Core` returned 30 packages, `AspNetSlice` returned 17 packages, `dotnet-slice` returned 114 packages, and `SliceFx` returned 0 packages.
  - Use `SliceFx.Core` for the core runtime package instead of `SliceFx.Core`.
  - Keep satellite package IDs under the same prefix: `SliceFx.SourceGenerator`, `SliceFx.Lambda`, `SliceFx.Lambda.FunctionPerFeature`, `SliceFx.TestHost`, `SliceFx.Wasi`, and `SliceFx.Cli`.
  - Public API namespace is `SliceFx` and the CLI command is `slicefx`.
  - Repository/docs identity is `sano-suguru/slicefx` and `https://sano-suguru.github.io/slicefx/`.
  - GitHub repository has been renamed to `sano-suguru/slicefx`; confirm Pages deploys successfully at the new project URL before publishing packages.
  - Scorecard:
    | Option | Exact package availability | Search noise | Positioning fit | Decision |
    | --- | --- | --- | --- | --- |
    | `SliceFx.Core` | Available, but `Slice` is taken | High | Clear to existing vertical-slice users, weak for search | Replace before preview |
    | `SliceFx.Core` | Available | Lowest observed (`SliceFx` returned 0 packages) | Brandable; needs subtitle for meaning | Adopt |
    | `AspNetSliceFx.Core` | Available | Medium; nearby ASP.NET vertical-slice packages exist | Clear for ASP.NET, too narrow for WASI/Lambda portability | Reject |
    | `DotNetSliceFx.Core` / `dotnet-slice` | Available | High | Repo/tool-like, less natural as package prefix | Reject |
- [ ] Freeze the preview version and scope.
  - Current repository metadata uses `0.1.0-preview.1`.
  - Preview scope should cover only the implemented experimental packages and documented limitations.
- [x] Review public API names before the first package push.
  - Core runtime: `[Feature]`, `[Filter<T>]`, validation types (`ISliceValidator<T>`, `SliceValidationResult`).
  - Generated API (emitted by the `SliceFx.SourceGenerator` package): `AddSlice`, `MapSlices`, route manifest (`GetSliceRoutesGenerated`), cross-assembly module helpers, WASI registrations (`AddSlice(WasiHostBuilder)`, `RegisterWasiRoutes`).
  - WASI API: `WasiHost`, `WasiApp`, `WasiRequest`, `WasiResponse`, `SliceResult`, `WasiRouteTable`.
  - Naming decision: keep `SliceFx.Wasi` namespace APIs / `Wasi*` types as the preview public API because the adapter targets any `wasi:http` host. `SliceFx.Workers` / `Worker*` would overfit the API to Cloudflare Workers, which is only one deployment target.
  - Before release, confirm no stale generic Workers naming remains outside this checklist:
    ```pwsh
    rg -n "Workers-portable|Workers portability|does not run on Workers|Generator_.*workers|WorkerHost|WorkerRoute|WorkerValidation|WorkerMissing|WorkerValidator|WorkerReflection|WorkerTyped|workersSource|Slice\\.Workers|WorkerApp|WorkerRequest|WorkerResponse|WorkerRouteTable" `
      README.md CHANGELOG.md CLAUDE.md docs tests src samples tools `
      --glob "!docs/oss-release-checklist.md"
    ```
    This should return no matches. References to Cloudflare Workers as a concrete deployment target are expected.
  - CLI API: `slicefx new feature`, `slicefx new filter`, `slicefx routes`, `slicefx client csharp`.
- [ ] Align docs and samples with the preview scope.
  - `README.md` quick start and status table.
  - `docs/product-direction.md` product claims and non-goals.
  - `CHANGELOG.md` release notes for the chosen preview version.
  - Sample ports, commands, and expected outputs.
  - WASI docs distinguish experimental `SliceFx.Wasi` package APIs from the unstable upstream WASI build/transpile toolchain.
- [ ] Keep public release messaging honest until NuGet publish is verified.
  - Do not claim that `dotnet add package SliceFx.Core` works before the package page exists.
  - Keep the website and README explicit that `0.1.0-preview.1` is unreleased.
  - Do not claim production adoption before public evidence exists.
- [ ] Publish at least one dogfooding note before claiming real-world usage.
  - Record project shape, routes implemented with Slice, friction points, and fixes made from dogfooding.
  - Until then, public adoption counts remain: Production adoption 0, published personal dogfooding logs 0.

## Local verification

Clean stale local artifacts before final verification, especially old `.nupkg` files under project output folders.

```pwsh
Get-ChildItem -Path . -Recurse -Filter *.nupkg |
  Where-Object { $_.FullName -match '\\(bin\\Release|nupkg)\\' } |
  Remove-Item

dotnet restore SliceFx.slnx
dotnet build SliceFx.slnx --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test SliceFx.slnx --configuration Release --no-build --no-restore
dotnet format SliceFx.slnx --verify-no-changes --no-restore --severity info --exclude-diagnostics CS1591

dotnet pack src\SliceFx.Core\SliceFx.Core.csproj --configuration Release --no-build
dotnet pack src\SliceFx.SourceGenerator\SliceFx.SourceGenerator.csproj --configuration Release --no-build
dotnet pack src\SliceFx.Lambda\SliceFx.Lambda.csproj --configuration Release --no-build
dotnet pack src\SliceFx.TestHost\SliceFx.TestHost.csproj --configuration Release --no-build
dotnet pack src\SliceFx.Wasi\SliceFx.Wasi.csproj --configuration Release --no-build
dotnet pack tools\SliceFx.Cli\SliceFx.Cli.csproj --configuration Release --no-build
```

- [ ] Confirm `src\SliceFx.Core\SliceFx.Core.csproj` still has no `<PackageReference>` entries.
- [ ] Confirm each generated package uses the same preview version.
- [ ] Inspect package metadata: README, license expression, repository URL, project URL, tags, and expected assemblies.
- [ ] Inspect the `SliceFx.SourceGenerator` package and confirm the generator is under `analyzers/dotnet/cs/`.
- [ ] Inspect the `SliceFx.Cli` package and confirm it is a .NET tool with command name `slicefx`.

## Smoke tests

Run the main user-facing flows before making a Go/No-Go decision.

```pwsh
dotnet run --project samples\SliceFx.Sample
curl.exe http://localhost:5099/health
curl.exe -X POST http://localhost:5099/users -H "Content-Type: application/json" -d "{\"name\":\"Alice\",\"email\":\"alice@example.com\"}"
curl.exe -X POST http://localhost:5099/users -H "Content-Type: application/json" -d "{\"name\":\"\",\"email\":\"not-an-email\"}"

dotnet run --project samples\SliceFx.TestHostSample
dotnet build samples\SliceFx.WasiSample  # Library — no entry point; build verifies compilation
dotnet run --project tools\SliceFx.Cli -- routes --project samples\SliceFx.Sample\SliceFx.Sample.csproj
dotnet run --project tools\SliceFx.Cli -- client csharp --project samples\SliceFx.Sample\SliceFx.Sample.csproj --output <temp-file> --force
```

- [ ] Main sample responds on `/health`.
- [ ] Main sample can create a user.
- [ ] Main sample returns validation Problem Details for invalid input.
- [ ] TestHost sample runs successfully.
- [ ] WASI sample dispatch runs successfully.
- [ ] CLI route listing and C# client generation work against the sample project.
- [ ] Lambda sample still starts locally on Kestrel if checked.

## Go/No-Go note

Before any publish action, write down:

- Package identity status.
- Final preview version.
- Verification command results.
- Smoke test results.
- Known issues or limitations to mention in release notes.
- A clear Go or No-Go recommendation.

## Final publish actions

Do these only after the pre-release gates are complete and the Go/No-Go note says Go.

- [ ] Run final CI from the actual Git repository.
- [ ] Create a GitHub release with the same version as the packages.
- [ ] Publish packages to NuGet after release notes are final.
- [ ] Verify the published package pages.

## GitHub Pages

The static landing page lives in `docs/index.html`. The Pages workflow deploys that directory through GitHub Actions.

Repository maintainers still need to enable GitHub Pages for the repository and select GitHub Actions as the Pages source.
