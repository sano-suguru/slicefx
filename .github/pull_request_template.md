## What changed

<!-- Describe the user-facing behavior, API surface, or internal invariant this changes. -->

## Verification

<!-- List the commands or smoke checks you ran. For example: dotnet build, dotnet format --verify-no-changes, sample curl checks. -->

## Discipline checklist

- [ ] `dotnet build SliceFx.slnx` passes.
- [ ] Formatting/linting is clean: `dotnet format SliceFx.slnx --verify-no-changes --no-restore --severity info --exclude-diagnostics CS1591` passes.
- [ ] `SliceFx.Core` remains zero-dependency: no `PackageReference` was added.
- [ ] Generated and runtime registration paths still match when registration behavior changes.
- [ ] Public API or sample changes are reflected in README/docs where relevant.
- [ ] Breaking changes are called out explicitly.
