# Development discipline

This project is experimental, so discipline should come from small automated checks and short review habits rather than heavyweight process.

## Definition of Done

A change is done when:

1. `dotnet build Slice.slnx` passes.
2. Formatting and style analyzers are clean: run `dotnet format Slice.slnx --verify-no-changes --no-restore --severity info --exclude-diagnostics CS1591`, or run `dotnet format` if formatting was intentionally updated.
3. `Slice.Core` still has no `PackageReference` items.
4. Runtime fallback and source-generated registration behavior stay aligned when feature registration, validation, filters, or metadata change.
5. Samples and README/docs are updated when public behavior changes.
6. Any breaking change is documented in the PR description.

## Invariants to protect

- `Slice.Core` stays dependency-free except for `Microsoft.AspNetCore.App`.
- Per-request code must not use reflection.
- Feature handlers remain `public static` and receive dependencies as parameters.
- Cross-cutting behavior uses ASP.NET Core endpoint filters, not new mediator-style abstractions.
- Generated registrations should mirror the runtime fallback's validation, filter, and metadata order.

## Local verification

```bash
dotnet restore Slice.slnx
dotnet build Slice.slnx --no-restore
dotnet format Slice.slnx --verify-no-changes --no-restore --severity info --exclude-diagnostics CS1591
```

The repository treats warnings as errors, enables the .NET analyzers at the `latest-recommended` level, and promotes code-style diagnostics to build-visible warnings.

For user-facing behavior, run the relevant sample and smoke-test the affected route.

```bash
dotnet run --project samples/Slice.Sample
curl http://localhost:5099/health
```

## Review rules

- Keep changes vertical and focused.
- Prefer tests or sample coverage for framework behavior changes.
- Do not merge changes that weaken the project invariants without documenting the tradeoff.
- Treat source generator and runtime fallback changes as paired work unless the PR explains why only one path is affected.
