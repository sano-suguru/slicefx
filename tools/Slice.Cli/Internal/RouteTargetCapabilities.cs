namespace Slice.Cli.Internal;

internal static class RouteTargetCapabilities
{
    internal const string Eligible = "eligible";
    internal const string Ineligible = "ineligible";
    internal const string Unknown = "unknown";

    internal static RouteCapabilities Classify(SliceRouteInfo route)
    {
        var wasi = new RouteCapability(route.Portability, route.PortabilityReason);
        var lambdaHostedApp = new RouteCapability(Eligible, null);
        var lambdaPerFeature = ClassifyLambdaPerFeature(route);

        return new RouteCapabilities(wasi, lambdaHostedApp, lambdaPerFeature);
    }

    private static RouteCapability ClassifyLambdaPerFeature(SliceRouteInfo route)
    {
        if (string.IsNullOrWhiteSpace(route.ReturnType))
        {
            return new RouteCapability(Unknown, "Handle method not found");
        }

        if (route.Portability == RouteCatalog.PortabilityAspNetOnly ||
            route.ReturnType.Contains("IResult", StringComparison.Ordinal))
        {
            return new RouteCapability(Ineligible, route.PortabilityReason ?? "returns ASP.NET IResult");
        }

        if (route.Filters.Any(static filter => !IsSliceValidatorFilter(filter)))
        {
            return new RouteCapability(Ineligible, "non-validator endpoint filters require the ASP.NET endpoint filter pipeline");
        }

        return new RouteCapability(Eligible, null);
    }

    private static bool IsSliceValidatorFilter(string filter)
        => filter.StartsWith("SliceValidatorFilter<", StringComparison.Ordinal)
           || filter.StartsWith("Slice.SliceValidatorFilter<", StringComparison.Ordinal)
           || filter.StartsWith("global::Slice.SliceValidatorFilter<", StringComparison.Ordinal);
}

internal sealed record RouteCapabilities(
    RouteCapability WasiDispatch,
    RouteCapability LambdaHostedApp,
    RouteCapability LambdaPerFeature);

internal sealed record RouteCapability(string Status, string? Reason);
