# Contributing to Slice

Thanks for your interest in Slice. This project is experimental, so the best contributions are focused, small, and explicit about the behavior they change.

## Before opening a PR

1. Keep `Slice.Core` dependency-free except for `Microsoft.AspNetCore.App`.
2. Do not add mediator-style abstractions; use ASP.NET Core endpoint filters for cross-cutting behavior.
3. Keep per-request paths reflection-free.
4. Keep runtime fallback and generated registration behavior aligned when changing routing, validation, filters, or metadata.
5. Update samples and docs when public behavior changes.

## Definition of Done

A change is done when:

1. `dotnet build Slice.slnx --configuration Release --no-restore -p:ContinuousIntegrationBuild=true` passes.
2. `dotnet test Slice.slnx --configuration Release --no-build --no-restore` passes.
3. Formatting and style analyzers are clean with `dotnet format Slice.slnx --verify-no-changes --no-restore --severity info --exclude-diagnostics CS1591`, or formatting was intentionally applied.
4. `src\Slice.Core\Slice.Core.csproj` still has no `<PackageReference>` items.
5. Runtime fallback and source-generated registration behavior stay aligned when feature registration, validation, filters, or metadata change.
6. Samples and README/docs are updated when public behavior changes.
7. Any breaking change is documented in the PR description.

## Invariants to protect

- `Slice.Core` stays dependency-free except for `Microsoft.AspNetCore.App`.
- Per-request code must not use reflection.
- Feature handlers remain `public static` and receive dependencies as parameters.
- Cross-cutting behavior uses ASP.NET Core endpoint filters, not new mediator-style abstractions.
- Generated registrations should mirror the runtime fallback's validation, filter, and metadata order.

## Local verification

```pwsh
dotnet restore Slice.slnx
dotnet build Slice.slnx --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test Slice.slnx --configuration Release --no-build --no-restore
dotnet format Slice.slnx --verify-no-changes --no-restore --severity info --exclude-diagnostics CS1591
```

For user-facing behavior, run the affected sample and smoke-test the route:

```pwsh
dotnet run --project samples\Slice.Sample
curl.exe http://localhost:5099/health
```

## Pull request guidance

- Describe the user-facing behavior, API surface, or invariant that changed.
- List the commands or smoke checks you ran.
- Call out breaking changes explicitly.
- Prefer tests or sample coverage for framework behavior changes.
