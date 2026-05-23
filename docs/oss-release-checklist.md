# OSS pre-release readiness checklist

Slice is pre-1.0 experimental software. Use `0.x` preview versions until the public API is intentionally stabilized.

This checklist is for deciding whether the repository is ready for a preview release. It intentionally separates pre-release preparation from the final publish actions.

Current Go/No-Go: **No-Go** until local verification, smoke tests, release notes, and NuGet publish verification are complete.

## Repository readiness

- [x] Add an MIT `LICENSE` file.
- [x] Add contribution, security, code of conduct, and changelog documents.
- [x] Keep CI build and formatter checks in `.github/workflows/ci.yml`.
- [x] Keep `Slice.Core` dependency-free except for `Microsoft.AspNetCore.App`.
- [x] Add package metadata for NuGet preview packages.
- [x] Add a GitHub Pages landing page under `docs/`.

## Pre-release gates

- [x] Confirm the core NuGet package identity before publishing anything.
  - `Slice` already exists on NuGet and is associated with an older unrelated package.
  - Use `Slice.Core` for the core runtime package instead of `Slice`.
  - Keep satellite package IDs under the same prefix: `Slice.SourceGenerator`, `Slice.Lambda`, `Slice.TestHost`, `Slice.Wasi`, and `Slice.Cli`.
- [ ] Freeze the preview version and scope.
  - Current repository metadata uses `0.1.0-preview.1`.
  - Preview scope should cover only the implemented experimental packages and documented limitations.
- [x] Review public API names before the first package push.
  - Core runtime: `[Feature]`, `[Filter<T>]`, validation types (`DataAnnotationsValidationFilter`, `ISliceValidator<T>`, `SliceValidatorFilter<T>`).
  - Generated API (emitted by `Slice.SourceGenerator`): `AddSlice`, `MapSlices`, route manifest (`GetSliceRoutesGenerated`), cross-assembly module helpers, WASI registrations (`AddSlice(WasiHostBuilder)`, `RegisterWasiRoutes`).
  - WASI API: `WasiHost`, `WasiApp`, `WasiRequest`, `WasiResponse`, `SliceResult`, `WasiRouteTable`.
  - Naming decision: keep `Slice.Wasi` / `Wasi*` as the preview public API because the adapter targets any `wasi:http` host. `Slice.Workers` / `Worker*` would overfit the API to Cloudflare Workers, which is only one deployment target.
  - Before release, confirm no stale generic Workers naming remains outside this checklist:
    ```pwsh
    rg -n "Workers-portable|Workers portability|does not run on Workers|Generator_.*workers|WorkerHost|WorkerRoute|WorkerValidation|WorkerMissing|WorkerValidator|WorkerReflection|WorkerTyped|workersSource|Slice\\.Workers|WorkerApp|WorkerRequest|WorkerResponse|WorkerRouteTable" `
      README.md CHANGELOG.md CLAUDE.md docs tests src samples tools `
      --glob "!docs/oss-release-checklist.md"
    ```
    This should return no matches. References to Cloudflare Workers as a concrete deployment target are expected.
  - CLI API: `slice new feature`, `slice new filter`, `slice routes`, `slice client csharp`.
- [ ] Align docs and samples with the preview scope.
  - `README.md` quick start and status table.
  - `docs/product-direction.md` product claims and non-goals.
  - `CHANGELOG.md` release notes for the chosen preview version.
  - Sample ports, commands, and expected outputs.
- [ ] Keep public release messaging honest until NuGet publish is verified.
  - Do not claim that `dotnet add package Slice.Core` works before the package page exists.
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

dotnet restore Slice.slnx
dotnet build Slice.slnx --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test Slice.slnx --configuration Release --no-build --no-restore
dotnet format Slice.slnx --verify-no-changes --no-restore --severity info --exclude-diagnostics CS1591

dotnet pack src\Slice.Core\Slice.Core.csproj --configuration Release --no-build
dotnet pack src\Slice.SourceGenerator\Slice.SourceGenerator.csproj --configuration Release --no-build
dotnet pack src\Slice.Lambda\Slice.Lambda.csproj --configuration Release --no-build
dotnet pack src\Slice.TestHost\Slice.TestHost.csproj --configuration Release --no-build
dotnet pack src\Slice.Wasi\Slice.Wasi.csproj --configuration Release --no-build
dotnet pack tools\Slice.Cli\Slice.Cli.csproj --configuration Release --no-build
```

- [ ] Confirm `src\Slice.Core\Slice.Core.csproj` still has no `<PackageReference>` entries.
- [ ] Confirm each generated package uses the same preview version.
- [ ] Inspect package metadata: README, license expression, repository URL, project URL, tags, and expected assemblies.
- [ ] Inspect the `Slice.SourceGenerator` package and confirm the generator is under `analyzers/dotnet/cs/`.
- [ ] Inspect the `Slice.Cli` package and confirm it is a .NET tool with command name `slice`.

## Smoke tests

Run the main user-facing flows before making a Go/No-Go decision.

```pwsh
dotnet run --project samples\Slice.Sample
curl.exe http://localhost:5099/health
curl.exe -X POST http://localhost:5099/users -H "Content-Type: application/json" -d "{\"name\":\"Alice\",\"email\":\"alice@example.com\"}"
curl.exe -X POST http://localhost:5099/users -H "Content-Type: application/json" -d "{\"name\":\"\",\"email\":\"not-an-email\"}"

dotnet run --project samples\Slice.TestHostSample
dotnet build samples\Slice.WasiSample  # Library — no entry point; build verifies compilation
dotnet run --project tools\Slice.Cli -- routes --project samples\Slice.Sample\Slice.Sample.csproj
dotnet run --project tools\Slice.Cli -- client csharp --project samples\Slice.Sample\Slice.Sample.csproj --output <temp-file> --force
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
