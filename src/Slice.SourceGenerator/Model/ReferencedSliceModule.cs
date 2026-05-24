using System.Collections.Immutable;

namespace Slice.SourceGenerator;

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

internal enum SliceGenerationRole
{
    Host,
    Feature,
    Both,
}
