using System.Text.RegularExpressions;

namespace Slice.Cli.Internal;

internal static partial class RouteCatalog
{
    internal const string PortabilityPortable = "portable";
    internal const string PortabilityPartial = "partial";
    internal const string PortabilityAspNetOnly = "aspnet-only";
    internal const string PortabilityUnknown = "unknown";
    internal const string LambdaEligible = "eligible";
    internal const string LambdaIneligible = "ineligible";
    internal const string LambdaUnknown = "unknown";

    private static readonly HashSet<string> s_simpleTypes = new(StringComparer.Ordinal)
    {
        "string", "Guid",
        "int", "long", "short", "uint", "ulong", "ushort",
        "bool", "double", "float", "decimal", "byte", "char",
        "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan", "Uri",
        "System.String", "System.Guid",
        "System.Int32", "System.Int64", "System.Int16",
        "System.UInt32", "System.UInt64", "System.UInt16",
        "System.Boolean", "System.Double", "System.Single",
        "System.Decimal", "System.Byte", "System.Char",
        "System.DateTime", "System.DateTimeOffset",
        "System.DateOnly", "System.TimeOnly", "System.TimeSpan", "System.Uri",
    };

    internal static SliceRouteInfo[] Discover(ProjectContext ctx)
        => DiscoverDetailed(ctx).Routes;

    internal static RouteCatalogDiscovery DiscoverDetailed(ProjectContext ctx)
    {
        var generatedRoutes = GeneratedRouteCatalog.Discover(ctx);
        return generatedRoutes.Found
            ? new RouteCatalogDiscovery(generatedRoutes.Routes, HasGeneratedMetadata: true, generatedRoutes.HasLambdaPerFunctionHandlers)
            : new RouteCatalogDiscovery(DiscoverFromSource(ctx), HasGeneratedMetadata: false, HasLambdaPerFunctionHandlers: false);
    }

    private static SliceRouteInfo[] DiscoverFromSource(ProjectContext ctx)
    {
        var featuresDir = Path.Combine(ctx.ProjectDirectory.FullName, "Features");
        if (!Directory.Exists(featuresDir))
        {
            throw new CliException($"Features directory not found: {featuresDir}");
        }

        return [.. Directory.EnumerateFiles(featuresDir, "*.cs", SearchOption.AllDirectories)
            .SelectMany(ReadRoutes)
            .OrderBy(static route => route.Pattern, StringComparer.Ordinal)
            .ThenBy(static route => route.Method, StringComparer.Ordinal)
            .ThenBy(static route => route.EndpointName, StringComparer.Ordinal)];
    }

    private static IEnumerable<SliceRouteInfo> ReadRoutes(string file)
    {
        var source = File.ReadAllText(file);
        foreach (Match featureMatch in FeatureAttributeRegex().Matches(source))
        {
            var route = UnescapeCSharpString(featureMatch.Groups["route"].Value);
            var parts = route.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            var classMatch = ClassRegex().Match(source, featureMatch.Index);
            if (!classMatch.Success)
            {
                continue;
            }

            var namespaceMatch = NamespaceRegex().Match(source);
            var @namespace = namespaceMatch.Success ? namespaceMatch.Groups["namespace"].Value : "";
            var args = featureMatch.Groups["args"].Value;
            var tag = ReadNamedStringArgument(args, "Tag") ?? InferTag(@namespace);
            var summary = ReadNamedStringArgument(args, "Summary");
            var handleMatch = HandleRegex().Match(source, classMatch.Index);
            var returnType = handleMatch.Success ? NormalizeWhitespace(handleMatch.Groups["return"].Value) : "";
            var parameters = handleMatch.Success ? ReadParameters(handleMatch.Groups["params"].Value) : [];
            var requestType = FindRequestType(parameters);
            var attributeBlock = ReadClassAttributeBlock(source, classMatch.Index);
            var filters = FilterRegex().Matches(attributeBlock)
                .Select(static match => match.Groups["filter"].Value.Trim())
                .ToArray();
            var (portability, portabilityReason) = ClassifyPortability(returnType, filters);
            var featureName = classMatch.Groups["class"].Value;
            var lambda = ClassifyLambdaPerFeature(returnType, filters, parameters, parts[1]);

            yield return new SliceRouteInfo(
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
                parameters,
                lambda.Status,
                lambda.Reason);
        }
    }

    private static SliceRouteParameter[] ReadParameters(string parameters)
    {
        return [.. SplitParameterList(parameters)
            .Select(ReadParameter)
            .Where(static parameter => parameter is not null)
            .Cast<SliceRouteParameter>()];
    }

    private static SliceRouteParameter? ReadParameter(string parameter)
    {
        var normalized = NormalizeWhitespace(parameter);
        if (normalized.StartsWith("this ", StringComparison.Ordinal))
        {
            normalized = normalized["this ".Length..];
        }

        var separator = normalized.LastIndexOf(' ');
        return separator < 0
            ? null
            : new SliceRouteParameter(normalized[..separator], normalized[(separator + 1)..]);
    }

    private static IEnumerable<string> SplitParameterList(string parameters)
    {
        var start = 0;
        var genericDepth = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var ch = parameters[i];
            if (ch == '<')
            {
                genericDepth++;
            }
            else if (ch == '>' && genericDepth > 0)
            {
                genericDepth--;
            }
            else if (ch == ',' && genericDepth == 0)
            {
                var segment = parameters[start..i].Trim();
                if (segment.Length > 0)
                {
                    yield return segment;
                }

                start = i + 1;
            }
        }

        var last = parameters[start..].Trim();
        if (last.Length > 0)
        {
            yield return last;
        }
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
            ? (PortabilityPartial, "non-validator endpoint filters do not run in the WASI path")
            : (PortabilityPortable, null);
    }

    internal static RouteCapability ClassifyLambdaPerFeature(
        string returnType,
        string[] filters,
        SliceRouteParameter[] parameters,
        string pattern)
    {
        if (returnType.Length == 0)
        {
            return new RouteCapability(LambdaUnknown, "Handle method not found");
        }

        if (returnType.Contains("IResult", StringComparison.Ordinal))
        {
            return new RouteCapability(LambdaIneligible, "returns ASP.NET IResult");
        }

        if (filters.Any(static filter => !IsSliceValidatorFilter(filter)))
        {
            return new RouteCapability(LambdaIneligible, "non-validator endpoint filters require the ASP.NET endpoint filter pipeline");
        }

        foreach (var parameter in parameters)
        {
            if (parameter.Type is "CancellationToken" or "System.Threading.CancellationToken"
                || parameter.Type == "Request"
                || parameter.Type.EndsWith(".Request", StringComparison.Ordinal))
            {
                continue;
            }

            if (IsRouteParam(parameter.Name, pattern)
                && !IsSimpleType(parameter.Type))
            {
                return new RouteCapability(
                    LambdaIneligible,
                    $"route parameter '{parameter.Name}' has unsupported type '{parameter.Type}'");
            }
        }

        return new RouteCapability(LambdaEligible, null);
    }

    private static bool IsSliceValidatorFilter(string filter)
        => (filter.StartsWith("SliceValidatorFilter<", StringComparison.Ordinal) &&
            filter.EndsWith('>'))
           || filter.StartsWith("Slice.SliceValidatorFilter<", StringComparison.Ordinal)
           || filter.StartsWith("global::Slice.SliceValidatorFilter<", StringComparison.Ordinal);

    private static bool IsSimpleType(string type)
        => s_simpleTypes.Contains(type)
           || (type.EndsWith('?') && s_simpleTypes.Contains(type[..^1]))
           || IsNullableType(type);

    private static bool IsNullableType(string type)
    {
        const string nullablePrefix = "System.Nullable<";
        if (!type.StartsWith(nullablePrefix, StringComparison.Ordinal)
            || !type.EndsWith('>'))
        {
            return false;
        }

        return s_simpleTypes.Contains(type[nullablePrefix.Length..^1]);
    }

    private static bool IsRouteParam(string name, string pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '{')
            {
                continue;
            }

            if (i + 1 < pattern.Length && pattern[i + 1] == '{')
            {
                i++;
                continue;
            }

            var end = pattern.IndexOf('}', i + 1);
            if (end < 0)
            {
                return false;
            }

            var parameterName = NormalizeRouteParameterName(pattern[(i + 1)..end]);
            if (string.Equals(parameterName, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            i = end;
        }

        return false;
    }

    private static string NormalizeRouteParameterName(string token)
    {
        token = token.TrimStart('*');
        var terminator = token.IndexOfAny([':', '?', '=']);
        return terminator >= 0 ? token[..terminator] : token;
    }

    private static string ReadClassAttributeBlock(string source, int classIndex)
    {
        var blockStart = 0;
        for (var index = classIndex - 1; index >= 0; index--)
        {
            if (source[index] is '}' or ';')
            {
                blockStart = index + 1;
                break;
            }
        }

        return source[blockStart..classIndex];
    }

    private static string UnescapeCSharpString(string value)
    {
        return value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace(@"\\", @"\", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\[Feature\(\s*""(?<route>(?:\\.|[^""\\])*)""(?<args>(?:[^\)""]|""(?:\\.|[^""\\])*"")*)\)\]")]
    private static partial Regex FeatureAttributeRegex();

    [GeneratedRegex(@"\bpublic\s+(?=[A-Za-z0-9_\s]*\bstatic\b)(?:(?:static|partial|unsafe)\s+)+class\s+(?<class>[A-Za-z_][A-Za-z0-9_]*)\b")]
    private static partial Regex ClassRegex();

    [GeneratedRegex(@"\bnamespace\s+(?<namespace>[A-Za-z_][A-Za-z0-9_.]*)\s*;")]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex(@"\[Filter<(?<filter>[^\]]+)>\]")]
    private static partial Regex FilterRegex();

    [GeneratedRegex(@"\bpublic\s+static\s+(?:async\s+)?(?<return>[A-Za-z0-9_<>,\.\?\s:\[\]]+?)\s+Handle\s*\((?<params>[^)]*)\)")]
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
    SliceRouteParameter[] Parameters,
    string? LambdaPerFeatureStatus = null,
    string? LambdaPerFeatureReason = null,
    string? SourceAssemblyName = null,
    string? LambdaPerFeatureHandlerType = null,
    string? LambdaPerFeatureHandlerMethod = null,
    string? ManifestSchemaVersion = null,
    string? WasiDispatchStatus = null,
    string? WasiDispatchReason = null,
    bool HasGeneratedMetadata = false)
{
    internal string FeatureType => $"{Namespace}.{FeatureName}";

    internal string? LambdaPerFeatureHandler
        => SourceAssemblyName is null ||
           LambdaPerFeatureHandlerType is null ||
           LambdaPerFeatureHandlerMethod is null
            ? null
            : $"{SourceAssemblyName}::{LambdaPerFeatureHandlerType}::{LambdaPerFeatureHandlerMethod}";
}

internal sealed record SliceRouteParameter(string Type, string Name);

internal sealed record RouteCatalogDiscovery(
    SliceRouteInfo[] Routes,
    bool HasGeneratedMetadata,
    bool HasLambdaPerFunctionHandlers);
