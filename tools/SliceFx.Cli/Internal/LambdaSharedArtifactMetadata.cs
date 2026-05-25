namespace SliceFx.Cli.Internal;

internal sealed record LambdaSharedArtifactMetadata(
    string ArtifactId,
    string ArtifactLayout,
    string CodeUri,
    string BootstrapMode,
    string? RuntimeIdentifier);

internal static class LambdaSharedArtifactMetadataValidator
{
    internal static LambdaSharedArtifactMetadata Validate(SliceRouteInfo[] routes, string? runtimeIdentifierOverride = null)
    {
        foreach (var route in routes)
        {
            ValidateValue(route, "artifactId", route.LambdaFunctionPerFeatureArtifactId, RouteCatalog.LambdaArtifactIdShared);
            ValidateValue(route, "artifactLayout", route.LambdaFunctionPerFeatureArtifactLayout, RouteCatalog.LambdaArtifactLayoutShared);
            ValidateValue(route, "codeUri", route.LambdaFunctionPerFeatureArtifactCodeUri, RouteCatalog.LambdaArtifactCodeUriShared);
            ValidateValue(route, "bootstrapMode", route.LambdaFunctionPerFeatureBootstrapMode, RouteCatalog.LambdaBootstrapModeGeneratedHandler);
        }

        var routeRuntimeIdentifiers = routes
            .Select(static route => route.LambdaFunctionPerFeatureRuntimeIdentifier)
            .Where(static runtimeIdentifier => !string.IsNullOrWhiteSpace(runtimeIdentifier))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (runtimeIdentifierOverride is null && routeRuntimeIdentifiers.Length > 1)
        {
            throw new CliException("Shared Lambda artifact metadata expected one runtime identifier, but generated metadata contains multiple runtime identifier values.");
        }

        return new LambdaSharedArtifactMetadata(
            RouteCatalog.LambdaArtifactIdShared,
            RouteCatalog.LambdaArtifactLayoutShared,
            RouteCatalog.LambdaArtifactCodeUriShared,
            RouteCatalog.LambdaBootstrapModeGeneratedHandler,
            runtimeIdentifierOverride ?? routeRuntimeIdentifiers.SingleOrDefault());
    }

    private static void ValidateValue(SliceRouteInfo route, string fieldName, string? value, string expected)
    {
        if (value is null)
        {
            return;
        }

        if (string.Equals(value, expected, StringComparison.Ordinal))
        {
            return;
        }

        throw new CliException(
            $"Shared Lambda artifact metadata for route '{route.EndpointName}' expected {fieldName} '{expected}' but found '{value}'. " +
            "Rebuild the project with a compatible SliceFx.SourceGenerator or use a supported artifact layout.");
    }
}
