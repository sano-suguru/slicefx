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

        // Step 1: Strip the async wrapper (Task<T> / ValueTask<T>).
        if (TryUnwrapGeneric(returnType, "Task", out var inner) ||
            TryUnwrapGeneric(returnType, "System.Threading.Tasks.Task", out inner) ||
            TryUnwrapGeneric(returnType, "ValueTask", out inner) ||
            TryUnwrapGeneric(returnType, "System.Threading.Tasks.ValueTask", out inner))
        {
            returnType = inner;
        }
        else if (returnType is "Task" or "System.Threading.Tasks.Task" or "ValueTask" or "System.Threading.Tasks.ValueTask")
        {
            return "void";
        }

        // Step 2: Unwrap SliceResult<T> → T, or map non-generic SliceResult → "void".
        // The manifest stores global::-qualified FQNs; hand-authored source may use bare names.
        // StripGlobal normalises global::SliceFx.SliceResult<...> to SliceFx.SliceResult<...>.
        return UnwrapSliceResultType(returnType);
    }

    /// <summary>
    /// Strips the <c>SliceResult&lt;T&gt;</c> wrapper (returning <c>T</c>) or maps the non-generic
    /// <c>SliceResult</c> to <c>"void"</c>. Returns <paramref name="type"/> unchanged for all other types.
    /// </summary>
    /// <remarks>
    /// Handles the qualification forms the manifest emits (<c>global::SliceFx.SliceResult&lt;T&gt;</c>),
    /// namespace-qualified hand-authored forms (<c>SliceFx.SliceResult&lt;T&gt;</c>), and bare unqualified
    /// forms (<c>SliceResult&lt;T&gt;</c>) — mirroring how <see cref="UnwrapReturnType"/> handles
    /// both <c>Task</c> and <c>System.Threading.Tasks.Task</c>.
    /// </remarks>
    private static string UnwrapSliceResultType(string type)
    {
        var stripped = StripGlobal(type);  // "global::SliceFx.SliceResult<T>" → "SliceFx.SliceResult<T>"

        // Generic SliceResult<T> → T: try most-specific qualifier first, then bare name.
        if (TryUnwrapGeneric(stripped, "SliceFx.SliceResult", out var payload) ||
            TryUnwrapGeneric(stripped, "SliceResult", out payload))
        {
            return payload;
        }

        // Non-generic SliceResult (no body) → void.
        if (stripped is "SliceFx.SliceResult" or "SliceResult")
        {
            return "void";
        }

        return type;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the declared return type (after stripping the async
    /// Task/ValueTask wrapper) is a <c>SliceResult&lt;T&gt;</c> or non-generic <c>SliceResult</c>.
    /// </summary>
    /// <param name="returnType">The raw return type string from the manifest.</param>
    /// <param name="payload">
    /// When this method returns <see langword="true"/>: the inner payload type string
    /// (<c>T</c>) for generic <c>SliceResult&lt;T&gt;</c>, or <see langword="null"/> for
    /// non-generic <c>SliceResult</c>.
    /// </param>
    internal static bool TryGetSliceResultPayload(string returnType, out string? payload)
    {
        returnType = RouteCatalog.NormalizeWhitespace(returnType);

        // Strip the async Task/ValueTask wrapper (same logic as UnwrapReturnType step 1).
        if (TryUnwrapGeneric(returnType, "Task", out var inner) ||
            TryUnwrapGeneric(returnType, "System.Threading.Tasks.Task", out inner) ||
            TryUnwrapGeneric(returnType, "ValueTask", out inner) ||
            TryUnwrapGeneric(returnType, "System.Threading.Tasks.ValueTask", out inner))
        {
            returnType = inner;
        }
        else if (returnType is "Task" or "System.Threading.Tasks.Task" or
                 "ValueTask" or "System.Threading.Tasks.ValueTask")
        {
            // Plain Task (no generic arg) — not a SliceResult.
            payload = null;
            return false;
        }

        var stripped = StripGlobal(returnType);

        // Generic SliceResult<T> — payload = T.
        if (TryUnwrapGeneric(stripped, "SliceFx.SliceResult", out var p) ||
            TryUnwrapGeneric(stripped, "SliceResult", out p))
        {
            payload = p;
            return true;
        }

        // Non-generic SliceResult — status-only, no response body.
        if (stripped is "SliceFx.SliceResult" or "SliceResult")
        {
            payload = null;
            return true;
        }

        payload = null;
        return false;
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

    /// <summary>
    /// Returns <see langword="true"/> when the <em>unwrapped</em> return type cannot be represented as a
    /// typed client method — e.g. <c>WasiResponse</c> is a server-side transport record, not a wire payload.
    /// </summary>
    /// <remarks>
    /// Uses the short name (last dot-separated component) so that fully-qualified types such as
    /// <c>SliceFx.Wasi.WasiResponse</c> are matched while namespace prefixes like
    /// <c>MyApp.WasiResponseApp.Features.Foo.Response</c> are not.
    /// </remarks>
    internal static bool IsNonClientReturnType(string unwrappedReturnType)
        => ShortName(StripGlobal(unwrappedReturnType)) == "WasiResponse";

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
