using System.Collections.Immutable;

namespace Slice.SourceGenerator;

internal sealed record ReferencedSliceModule(
    string AssemblyName,
    string RegistrationTypeFqn,
    ImmutableArray<ReferencedSliceRoute> Routes,
    bool HasAspNetServices,
    bool HasAspNetRoutes,
    bool HasWorkerRoutes);

internal sealed record ReferencedSliceRoute(
    string AssemblyName,
    string EndpointName,
    string FeatureType,
    string HttpMethod,
    string Pattern);

internal enum SliceGenerationRole
{
    Host,
    Feature,
    Both,
}
