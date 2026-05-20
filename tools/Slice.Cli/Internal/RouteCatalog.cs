using System.Text.RegularExpressions;

namespace Slice.Cli.Internal;

internal static partial class RouteCatalog
{
    internal const string PortabilityPortable = "portable";
    internal const string PortabilityPartial = "partial";
    internal const string PortabilityAspNetOnly = "aspnet-only";
    internal const string PortabilityUnknown = "unknown";

    internal static SliceRouteInfo[] Discover(ProjectContext ctx)
    {
        var featuresDir = Path.Combine(ctx.ProjectDirectory.FullName, "Features");
        if (!Directory.Exists(featuresDir))
        {
            throw new CliException($"Features directory not found: {featuresDir}");
        }

        return [.. Directory.EnumerateFiles(featuresDir, "*.cs", SearchOption.AllDirectories)
            .Select(ReadRoute)
            .Where(static route => route is not null)
            .Cast<SliceRouteInfo>()
            .OrderBy(static route => route.Pattern, StringComparer.Ordinal)
            .ThenBy(static route => route.Method, StringComparer.Ordinal)];
    }

    private static SliceRouteInfo? ReadRoute(string file)
    {
        var source = File.ReadAllText(file);
        var featureMatch = FeatureAttributeRegex().Match(source);
        if (!featureMatch.Success)
        {
            return null;
        }

        var route = UnescapeCSharpString(featureMatch.Groups["route"].Value);
        var parts = route.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        var classMatch = ClassRegex().Match(source, featureMatch.Index);
        if (!classMatch.Success)
        {
            return null;
        }

        var namespaceMatch = NamespaceRegex().Match(source);
        var @namespace = namespaceMatch.Success ? namespaceMatch.Groups["namespace"].Value : "";
        var args = featureMatch.Groups["args"].Value;
        var tag = ReadNamedStringArgument(args, "Tag") ?? InferTag(@namespace);
        var summary = ReadNamedStringArgument(args, "Summary");
        var handleMatch = HandleRegex().Match(source);
        var returnType = handleMatch.Success ? NormalizeWhitespace(handleMatch.Groups["return"].Value) : "";
        var parameters = handleMatch.Success ? ReadParameters(handleMatch.Groups["params"].Value) : [];
        var requestType = FindRequestType(parameters);
        var filters = FilterRegex().Matches(source)
            .Select(static match => match.Groups["filter"].Value.Trim())
            .ToArray();
        var (portability, portabilityReason) = ClassifyPortability(returnType, filters);
        var featureName = classMatch.Groups["class"].Value;

        return new SliceRouteInfo(
            parts[0].ToUpperInvariant(),
            parts[1],
            @namespace,
            featureName,
            tag,
            $"{tag}.{featureName}",
            summary,
            requestType,
            returnType,
            portability,
            portabilityReason,
            filters,
            parameters);
    }

    private static SliceRouteParameter[] ReadParameters(string parameters)
    {
        return [.. parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ReadParameter)
            .Where(static parameter => parameter is not null)
            .Cast<SliceRouteParameter>()];
    }

    private static SliceRouteParameter? ReadParameter(string parameter)
    {
        var normalized = NormalizeWhitespace(parameter);
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length < 2 ? null : new SliceRouteParameter(parts[^2], parts[^1]);
    }

    private static string? FindRequestType(SliceRouteParameter[] parameters)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.Type == "Request" || parameter.Type.EndsWith(".Request", StringComparison.Ordinal))
            {
                return parameter.Type;
            }
        }

        return null;
    }

    private static string? ReadNamedStringArgument(string args, string name)
    {
        var match = Regex.Match(args, $@"\b{name}\s*=\s*""(?<value>(?:\\.|[^""\\])*)""");
        return match.Success ? UnescapeCSharpString(match.Groups["value"].Value) : null;
    }

    private static string InferTag(string @namespace)
    {
        var idx = @namespace.IndexOf(".Features.", StringComparison.Ordinal);
        if (idx < 0)
        {
            return "Default";
        }

        var rest = @namespace[(idx + ".Features.".Length)..];
        var dot = rest.IndexOf('.');
        return dot < 0 ? rest : rest[..dot];
    }

    internal static string NormalizeWhitespace(string value)
        => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static (string Status, string? Reason) ClassifyPortability(string returnType, string[] filters)
    {
        if (returnType.Length == 0)
        {
            return (PortabilityUnknown, "Handle method not found");
        }

        if (returnType.Contains("IResult", StringComparison.Ordinal))
        {
            return (PortabilityAspNetOnly, "returns ASP.NET IResult");
        }

        var hasAspNetFilter = filters.Any(static filter =>
            !filter.StartsWith("SliceValidatorFilter<", StringComparison.Ordinal) &&
            !filter.StartsWith("Slice.SliceValidatorFilter<", StringComparison.Ordinal) &&
            !filter.StartsWith("global::Slice.SliceValidatorFilter<", StringComparison.Ordinal));

        return hasAspNetFilter
            ? (PortabilityPartial, "non-validator endpoint filters do not run in Workers")
            : (PortabilityPortable, null);
    }

    private static string UnescapeCSharpString(string value)
    {
        return value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace(@"\\", @"\", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\[Feature\(\s*""(?<route>(?:\\.|[^""\\])*)""(?<args>(?:[^\)""]|""(?:\\.|[^""\\])*"")*)\)\]")]
    private static partial Regex FeatureAttributeRegex();

    [GeneratedRegex(@"\bpublic\s+static\s+class\s+(?<class>[A-Za-z_][A-Za-z0-9_]*)\b")]
    private static partial Regex ClassRegex();

    [GeneratedRegex(@"\bnamespace\s+(?<namespace>[A-Za-z_][A-Za-z0-9_.]*)\s*;")]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex(@"\[Filter<(?<filter>[^\]]+)>\]")]
    private static partial Regex FilterRegex();

    [GeneratedRegex(@"\bpublic\s+static\s+(?:async\s+)?(?<return>[A-Za-z0-9_<>,\.\?\s]+?)\s+Handle\s*\((?<params>[^)]*)\)")]
    private static partial Regex HandleRegex();
}

internal sealed record SliceRouteInfo(
    string Method,
    string Pattern,
    string Namespace,
    string FeatureName,
    string Tag,
    string EndpointName,
    string? Summary,
    string? RequestType,
    string ReturnType,
    string Portability,
    string? PortabilityReason,
    string[] Filters,
    SliceRouteParameter[] Parameters)
{
    internal string FeatureType => $"{Namespace}.{FeatureName}";
}

internal sealed record SliceRouteParameter(string Type, string Name);
