# OSS pre-release readiness checklist

Slice is pre-1.0 experimental software. Use `0.x` preview versions until the public API is intentionally stabilized.

This checklist is for deciding whether the repository is ready for a preview release. It intentionally separates pre-release preparation from the final publish actions.

## Repository readiness

- [x] Add an MIT `LICENSE` file.
- [x] Add contribution, security, code of conduct, and changelog documents.
- [x] Keep CI build and formatter checks in `.github/workflows/ci.yml`.
- [x] Keep `Slice.Core` dependency-free except for `Microsoft.AspNetCore.App`.
- [x] Add package metadata for NuGet preview packages.
- [x] Add a GitHub Pages landing page under `docs/`.

## Pre-release gates

- [ ] Confirm the root NuGet package identity before publishing anything.
  - `Slice` already exists on NuGet and is associated with an older unrelated package.
  - Confirm maintainer ownership or obtain a transfer before using `Slice`.
  - If `Slice` cannot be used, choose a new package ID strategy before publishing satellite packages.
- [ ] Freeze the preview version and scope.
  - Current repository metadata uses `0.1.0-preview.1`.
  - Preview scope should cover only the implemented experimental packages and documented limitations.
- [ ] Review public API names before the first package push.
  - Core runtime: `[Feature]`, `[Filter<T>]`, `AddSlice`, `MapSlices`, validation types.
  - Generated API: `AddSliceGenerated`, `MapSlicesGenerated`, route manifest, Workers registrations.
  - Workers API: `WorkerHost`, `WorkerApp`, `WorkerRequest`, `WorkerResponse`, `SliceResult`, `WorkerRouteTable`.
  - CLI API: `slice new feature`, `slice new filter`, `slice routes`, `slice client csharp`.
- [ ] Align docs and samples with the preview scope.
  - `README.md` quick start and status table.
  - `docs/product-direction.md` product claims and non-goals.
  - `CHANGELOG.md` release notes for the chosen preview version.
  - Sample ports, commands, and expected outputs.

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
dotnet pack src\Slice.Workers\Slice.Workers.csproj --configuration Release --no-build
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
dotnet run --project samples\Slice.WorkersSample -- --probe /health
dotnet run --project tools\Slice.Cli -- routes --project samples\Slice.Sample\Slice.Sample.csproj
dotnet run --project tools\Slice.Cli -- client csharp --project samples\Slice.Sample\Slice.Sample.csproj --output <temp-file> --force
```

- [ ] Main sample responds on `/health`.
- [ ] Main sample can create a user.
- [ ] Main sample returns validation Problem Details for invalid input.
- [ ] TestHost sample runs successfully.
- [ ] Workers sample probe runs successfully.
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
