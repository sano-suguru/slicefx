# Contributing to SliceFx

Thanks for your interest in SliceFx. This project is experimental, so the best contributions are focused, small, and explicit about the behavior they change.

## Before opening a PR

1. Keep `SliceFx.Core` dependency-free except for `Microsoft.AspNetCore.App`.
2. Do not add mediator-style abstractions; use ASP.NET Core endpoint filters for cross-cutting behavior.
3. Keep per-request paths reflection-free.
4. Keep generated registrations and the generated route manifest aligned when changing routing, validation, filters, or metadata.
5. Update samples and docs when public behavior changes.

## Definition of Done

A change is done when:

1. `dotnet build SliceFx.slnx --configuration Release --no-restore -p:ContinuousIntegrationBuild=true` passes.
2. `dotnet test SliceFx.slnx --configuration Release --no-build --no-restore` passes.
3. Formatting and style analyzers are clean with `dotnet format SliceFx.slnx --verify-no-changes --no-restore --severity info --exclude-diagnostics CS1591`, or formatting was intentionally applied.
4. `src\SliceFx.Core\SliceFx.Core.csproj` still has no `<PackageReference>` items.
5. Generated registrations, route manifests, and CLI source-scanning fallback behavior stay aligned when feature registration, validation, filters, or metadata change.
6. Samples and README/docs are updated when public behavior changes.
7. Any breaking change is documented in the PR description.

## SDK and analyzer policy

SliceFx keeps warnings and code-analysis diagnostics as errors. To avoid surprise failures from newly promoted SDK analyzer rules, the normal build pins the analyzer recommendation set in `Directory.Build.props` instead of using `latest-recommended`.

The analyzer baseline should be bumped in a dedicated maintenance PR, not bundled with feature work. Review it quarterly by default, before public previews/releases, or when the analyzer canary reports diagnostics that are valuable for correctness, security, or maintainability. The canary is non-blocking and reports latest .NET 10 analyzer drift through the workflow summary and issue updates; it does not change the pinned baseline automatically.

`global.json` currently keeps `rollForward: latestFeature` so contributors can use newer .NET 10 feature bands. Analyzer churn is controlled by the pinned `AnalysisLevel`; revisit SDK roll-forward strictness only if feature-band drift becomes a recurring problem.

## Invariants to protect

- `SliceFx.Core` stays dependency-free except for `Microsoft.AspNetCore.App`.
- Per-request code must not use reflection.
- Feature handlers remain `public static` and receive dependencies as parameters.
- Cross-cutting behavior uses ASP.NET Core endpoint filters, not new mediator-style abstractions.
- Generated registrations are the ASP.NET endpoint registration path; the CLI source-scanning fallback is tooling-only and should mirror generated route manifest metadata for unbuilt projects.

## Local verification

```pwsh
dotnet restore SliceFx.slnx
dotnet build SliceFx.slnx --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
dotnet test SliceFx.slnx --configuration Release --no-build --no-restore
dotnet format SliceFx.slnx --verify-no-changes --no-restore --severity info --exclude-diagnostics CS1591
```

For user-facing behavior, run the affected sample and smoke-test the route:

```pwsh
dotnet run --project samples\SliceFx.Sample
curl.exe http://localhost:5099/health
```

## Pull request guidance

- Describe the user-facing behavior, API surface, or invariant that changed.
- List the commands or smoke checks you ran.
- Call out breaking changes explicitly.
- Prefer tests or sample coverage for framework behavior changes.
