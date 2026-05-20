# Contributing to Slice

Thanks for your interest in Slice. This project is experimental, so the best contributions are focused, small, and explicit about the behavior they change.

## Before opening a PR

1. Keep `Slice.Core` dependency-free except for `Microsoft.AspNetCore.App`.
2. Do not add mediator-style abstractions; use ASP.NET Core endpoint filters for cross-cutting behavior.
3. Keep per-request paths reflection-free.
4. Keep runtime fallback and generated registration behavior aligned when changing routing, validation, filters, or metadata.
5. Update samples and docs when public behavior changes.

## Local verification

```pwsh
dotnet restore Slice.slnx
dotnet build Slice.slnx --configuration Release --no-restore -p:ContinuousIntegrationBuild=true
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
