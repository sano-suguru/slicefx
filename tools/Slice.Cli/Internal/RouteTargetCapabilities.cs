namespace Slice.Cli.Internal;

internal static class RouteTargetCapabilities
{
    internal const string Eligible = "eligible";
    internal const string Ineligible = "ineligible";
    internal const string Unknown = "unknown";

    internal static RouteCapabilities Classify(SliceRouteInfo route)
    {
        var wasi = !string.IsNullOrWhiteSpace(route.WasiDispatchStatus)
            ? new RouteCapability(route.WasiDispatchStatus, route.WasiDispatchReason)
            : route.HasGeneratedMetadata
                ? new RouteCapability(Unknown, "WASI dispatch metadata missing")
                : new RouteCapability(route.Portability, route.PortabilityReason);
        var lambdaHostedApp = new RouteCapability(Eligible, null);
        var lambdaFunctionPerFeature = ClassifyLambdaFunctionPerFeature(route);

        return new RouteCapabilities(wasi, lambdaHostedApp, lambdaFunctionPerFeature);
    }

    private static RouteCapability ClassifyLambdaFunctionPerFeature(SliceRouteInfo route)
    {
        if (!string.IsNullOrWhiteSpace(route.LambdaFunctionPerFeatureStatus))
        {
            return new RouteCapability(route.LambdaFunctionPerFeatureStatus, route.LambdaFunctionPerFeatureReason);
        }

        if (string.IsNullOrWhiteSpace(route.ReturnType))
        {
            return new RouteCapability(Unknown, "Handle method not found");
        }

        if (route.Portability == RouteCatalog.PortabilityAspNetOnly ||
            route.ReturnType.Contains("IResult", StringComparison.Ordinal))
        {
            return new RouteCapability(Ineligible, route.PortabilityReason ?? "returns ASP.NET IResult");
        }

        return RouteCatalog.ClassifyLambdaFunctionPerFeature(route.ReturnType, route.Filters, route.Parameters, route.Pattern);
    }
}

internal sealed record RouteCapabilities(
    RouteCapability WasiDispatch,
    RouteCapability LambdaHostedApp,
    RouteCapability LambdaFunctionPerFeature);

internal sealed record RouteCapability(string Status, string? Reason);
