using System.Text.RegularExpressions;

namespace SliceFx.Cli.Internal;

internal static partial class ClientGenerationHelpers
{
    internal static readonly HashSet<string> SimpleParameterTypes =
    [
        "string", "Guid",
        "int", "long", "short", "uint", "ulong", "ushort",
        "bool", "double", "float", "decimal", "byte",
        "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan",
        "char", "Uri",
        "System.Guid", "System.String",
        "System.Int32", "System.Int64", "System.Int16",
        "System.UInt32", "System.UInt64", "System.UInt16",
        "System.Boolean", "System.Double", "System.Single", "System.Decimal",
        "System.Byte", "System.DateTime", "System.DateTimeOffset",
        "System.DateOnly", "System.TimeOnly", "System.TimeSpan",
        "System.Char", "System.Uri",
    ];

    internal static string UnwrapReturnType(string returnType)
    {
        returnType = RouteCatalog.NormalizeWhitespace(returnType);
        return TryUnwrapGeneric(returnType, "Task", out var taskType) ||
               TryUnwrapGeneric(returnType, "System.Threading.Tasks.Task", out taskType) ||
               TryUnwrapGeneric(returnType, "ValueTask", out taskType) ||
               TryUnwrapGeneric(returnType, "System.Threading.Tasks.ValueTask", out taskType)
            ? taskType
            : returnType is "Task" or "System.Threading.Tasks.Task" or "ValueTask" or "System.Threading.Tasks.ValueTask" ? "void" : returnType;
    }

    internal static bool TryUnwrapGeneric(string type, string wrapper, out string inner)
    {
        var prefix = wrapper + "<";
        if (type.StartsWith(prefix, StringComparison.Ordinal) && type.EndsWith('>'))
        {
            inner = type[prefix.Length..^1];
            return true;
        }

        inner = "";
        return false;
    }

    internal static SliceRouteParameter? FindBodyParameter(SliceRouteInfo route)
    {
        var explicitBody = route.Parameters.FirstOrDefault(static parameter => parameter.BindingSource == "body");
        if (explicitBody is not null)
        {
            return explicitBody;
        }

        return route.RequestType is null
            ? null
            : route.Parameters.FirstOrDefault(parameter => parameter.Type == route.RequestType);
    }

    internal static SliceRouteParameter[] FindRouteParameters(SliceRouteInfo route)
    {
        var parameters = RouteParameterRegex().Matches(route.Pattern)
            .Select(static match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. route.Parameters
            .Where(parameter => parameter.BindingSource == "route" || parameters.Contains(parameter.WireName))
        ];
    }

    internal static SliceRouteParameter[] FindHeaderParameters(SliceRouteInfo route)
        => [.. route.Parameters
            .Where(static parameter => parameter.BindingSource == "header")
            .Where(static parameter => IsSupportedQueryParameterType(parameter.Type))
        ];

    internal static SliceRouteParameter[] FindQueryParameters(
        SliceRouteInfo route,
        SliceRouteParameter[] routeParameters,
        SliceRouteParameter? bodyParameter)
    {
        var routeParameterNames = routeParameters
            .Select(static parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. route.Parameters
            .Where(parameter => !routeParameterNames.Contains(parameter.Name))
            .Where(static parameter => parameter.BindingSource is null or "query")
            .Where(parameter => bodyParameter is null || parameter.Name != bodyParameter.Name)
            .Where(static parameter => parameter.Type is not ("CancellationToken" or "System.Threading.CancellationToken"))
            .Where(static parameter => IsSupportedQueryParameterType(parameter.Type))
        ];
    }

    internal static bool IsSupportedQueryParameterType(string type)
    {
        var normalized = NormalizeParameterType(type);
        if (normalized.EndsWith("[]", StringComparison.Ordinal))
        {
            normalized = normalized[..^2];
        }

        return SimpleParameterTypes.Contains(normalized);
    }

    internal static string NormalizeParameterType(string type)
    {
        var normalized = RouteCatalog.NormalizeWhitespace(type);
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
        {
            normalized = normalized["global::".Length..];
        }

        if (normalized.EndsWith('?'))
        {
            normalized = normalized[..^1];
        }

        const string nullablePrefix = "System.Nullable<";
        if (normalized.StartsWith(nullablePrefix, StringComparison.Ordinal) && normalized.EndsWith('>'))
        {
            normalized = normalized[nullablePrefix.Length..^1];
        }

        return normalized;
    }

    internal static string StripGlobal(string type)
        => type.StartsWith("global::", StringComparison.Ordinal) ? type["global::".Length..] : type;

    internal static string ShortName(string type)
    {
        var stripped = StripGlobal(type);
        var dot = stripped.LastIndexOf('.');
        return dot >= 0 ? stripped[(dot + 1)..] : stripped;
    }

    internal static string ToPascalIdentifier(string value, string fallback)
    {
        var parts = IdentifierSeparatorRegex().Split(value)
            .Where(static part => part.Length > 0)
            .ToArray();
        if (parts.Length == 0)
        {
            return fallback;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            var normalized = char.ToUpperInvariant(part[0]) + part[1..];
            sb.Append(normalized);
        }

        if (sb.Length == 0 || !(char.IsLetter(sb[0]) || sb[0] == '_'))
        {
            sb.Insert(0, fallback);
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::[^}]+)?\}")]
    internal static partial Regex RouteParameterRegex();

    [GeneratedRegex(@"[^A-Za-z0-9_]+")]
    private static partial Regex IdentifierSeparatorRegex();
}
