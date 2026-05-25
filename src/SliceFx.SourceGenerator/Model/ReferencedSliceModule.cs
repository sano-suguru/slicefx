using System.Collections.Immutable;

namespace SliceFx.SourceGenerator;

internal sealed record ReferencedSliceModule(
    string AssemblyName,
    string RegistrationTypeFqn,
    ImmutableArray<ReferencedSliceRoute> Routes,
    ImmutableArray<ReferencedSliceValidator> Validators,
    bool HasAspNetServices,
    bool HasValidatorServices,
    bool HasAspNetRoutes,
    bool HasWasiRoutes);

internal sealed record ReferencedSliceRoute(
    string AssemblyName,
    string EndpointName,
    string FeatureType,
    string HttpMethod,
    string Pattern,
    string? RequestType);

internal sealed record ReferencedSliceValidator(
    string AssemblyName,
    string RequestType,
    string ValidatorType);

internal sealed record ReferencedSliceModulesResult(
    ImmutableArray<ReferencedSliceModule> Modules,
    ImmutableArray<EquatableDiagnostic> Diagnostics);

internal sealed record SliceReferenceAggregationOptions(
    bool AggregateFlagSpecified,
    bool AggregateAllReferences,
    string? InvalidAggregateReferencesValue,
    ImmutableHashSet<string> AllowedAssemblies)
{
    internal bool HasAllowList => AllowedAssemblies.Count > 0;
}

internal enum SliceGenerationRole
{
    Host,
    Feature,
    Both,
}
