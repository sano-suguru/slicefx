# OSS release checklist

Slice is pre-1.0 experimental software. Use `0.x` preview versions until the public API is intentionally stabilized.

## Repository readiness

- [x] Add an MIT `LICENSE` file.
- [x] Add contribution, security, code of conduct, and changelog documents.
- [x] Keep CI build and formatter checks in `.github/workflows/ci.yml`.
- [x] Keep `Slice.Core` dependency-free except for `Microsoft.AspNetCore.App`.
- [x] Add package metadata for NuGet preview packages.
- [x] Add a GitHub Pages landing page under `docs/`.

## Before the first public package push

- [ ] Decide the exact preview version, such as `0.1.0-preview.1`.
- [ ] Confirm package IDs are available on NuGet:
  - `Slice`
  - `Slice.SourceGenerator`
  - `Slice.Lambda`
  - `Slice.TestHost`
  - `Slice.Workers`
  - `Slice.Cli`
- [ ] Run local verification:

```pwsh
dotnet restore Slice.slnx
dotnet build Slice.slnx --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet format Slice.slnx --verify-no-changes --no-restore --severity info --exclude-diagnostics CS1591
dotnet pack src\Slice.Core\Slice.Core.csproj --configuration Release
dotnet pack src\Slice.SourceGenerator\Slice.SourceGenerator.csproj --configuration Release
dotnet pack src\Slice.Lambda\Slice.Lambda.csproj --configuration Release
dotnet pack src\Slice.TestHost\Slice.TestHost.csproj --configuration Release
dotnet pack src\Slice.Workers\Slice.Workers.csproj --configuration Release
dotnet pack tools\Slice.Cli\Slice.Cli.csproj --configuration Release
```

- [ ] Inspect the `Slice.SourceGenerator` package and confirm the generator is under `analyzers/dotnet/cs/`.
- [ ] Create a GitHub release with the same version as the packages.
- [ ] Publish packages to NuGet after the release notes are final.

## GitHub Pages

The static landing page lives in `docs/index.html`. The Pages workflow deploys that directory through GitHub Actions.

Repository maintainers still need to enable GitHub Pages for the repository and select GitHub Actions as the Pages source.
