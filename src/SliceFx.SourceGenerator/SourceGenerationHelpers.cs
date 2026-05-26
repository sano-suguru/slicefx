using SliceFx.Shared;

namespace SliceFx.SourceGenerator;

internal static class SourceGenerationHelpers
{
    public const string ManifestSchemaVersion = SliceRouteManifestSchema.CurrentVersion;
    public const string ManifestEligible = "eligible";
    public const string ManifestIneligible = "ineligible";
    public const string PortabilityPortable = "portable";
    public const string PortabilityPartial = "partial";
    public const string PortabilityAspNetOnly = "aspnet-only";

    private static readonly HashSet<string> s_simpleTypes = new(StringComparer.Ordinal)
    {
        "global::System.String", "global::System.Guid",
        "global::System.Int32", "global::System.Int64", "global::System.Int16",
        "global::System.UInt32", "global::System.UInt64", "global::System.UInt16",
        "global::System.Boolean", "global::System.Double", "global::System.Single",
        "global::System.Decimal", "global::System.Byte", "global::System.Char",
        "global::System.DateTime", "global::System.DateTimeOffset",
        "global::System.DateOnly", "global::System.TimeOnly", "global::System.TimeSpan",
        "global::System.Uri",
        "string", "int", "long", "short", "bool", "double", "float", "decimal",
    };

    public static string TrimGlobalAlias(string value)
        => value.Replace("global::", "");

    public static string ToLambdaArtifactId(string endpointName)
    {
        var chars = new List<char>(endpointName.Length);
        var lastWasDash = false;
        foreach (var ch in endpointName)
        {
            if (char.IsLetterOrDigit(ch))
            {
                chars.Add(char.ToLowerInvariant(ch));
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                chars.Add('-');
                lastWasDash = true;
            }
        }

        var value = new string([.. chars]).Trim('-');
        return value.Length == 0 ? "function" : value;
    }

    public static bool IsNonGenericAwaitable(string returnTypeFqn)
        => returnTypeFqn is "global::System.Threading.Tasks.Task"
        or "global::System.Threading.Tasks.ValueTask";

    public static bool IsGenericAwaitable(string returnTypeFqn)
        => returnTypeFqn.StartsWith("global::System.Threading.Tasks.Task<", StringComparison.Ordinal)
        || returnTypeFqn.StartsWith("global::System.Threading.Tasks.ValueTask<", StringComparison.Ordinal);

    public static string? GetAwaitedReturnType(string returnTypeFqn)
    {
        if (returnTypeFqn is "void"
            or "global::System.Threading.Tasks.Task"
            or "global::System.Threading.Tasks.ValueTask")
        {
            return null;
        }

        if (returnTypeFqn.StartsWith("global::System.Threading.Tasks.Task<", StringComparison.Ordinal))
        {
            return GetSingleGenericArgument(returnTypeFqn, "global::System.Threading.Tasks.Task<");
        }

        if (returnTypeFqn.StartsWith("global::System.Threading.Tasks.ValueTask<", StringComparison.Ordinal))
        {
            return GetSingleGenericArgument(returnTypeFqn, "global::System.Threading.Tasks.ValueTask<");
        }

        return returnTypeFqn;
    }

    public static bool IsSimpleType(string typeFqn)
        => s_simpleTypes.Contains(typeFqn) || IsSimpleNullableType(typeFqn);

    public static bool IsFrameworkType(string typeFqn)
    {
        var value = TrimGlobalAlias(typeFqn);
        return value.StartsWith("System.", StringComparison.Ordinal)
            || value.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    public static bool IsRequestLikeParameter(HandleParamModel parameter)
        => parameter.TypeFqn != "global::System.Threading.CancellationToken"
           && !parameter.IsInterfaceOrAbstract
           && !IsSimpleType(parameter.TypeFqn)
           && !IsFrameworkType(parameter.TypeFqn);

    public static bool IsRouteParam(string name, string pattern)
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

            var parameterName = NormalizeRouteParameterName(pattern.Substring(i + 1, end - i - 1));
            if (string.Equals(parameterName, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            i = end;
        }

        return false;
    }

    public static HandlerParameterBinding ResolveParameterBinding(
        HandleParamModel parameter,
        string pattern,
        string featureTypeFqn)
    {
        var wireName = string.IsNullOrWhiteSpace(parameter.BindingName) ? parameter.Name : parameter.BindingName!;
        return parameter.BindingSource switch
        {
            "body" => ResolveExplicitBody(parameter, wireName),
            "route" => ResolveExplicitScalar(parameter, pattern, wireName, HandlerParameterBindingSource.Route),
            "query" => ResolveExplicitScalar(parameter, pattern, wireName, HandlerParameterBindingSource.Query),
            "header" => ResolveExplicitScalar(parameter, pattern, wireName, HandlerParameterBindingSource.Header),
            "services" => new HandlerParameterBinding(HandlerParameterBindingSource.Services, wireName, null),
            _ => ResolveConventionBinding(parameter, pattern, featureTypeFqn, wireName),
        };
    }

    public static bool IsLambdaProxyResponseType(string? typeFqn)
        => typeFqn == "global::Amazon.Lambda.APIGatewayEvents.APIGatewayHttpApiV2ProxyResponse";

    public static bool IsWasiResponseType(string? typeFqn)
        => typeFqn == "global::SliceFx.Wasi.WasiResponse";

    public static string BindingSourceName(HandlerParameterBindingSource source)
    {
        if (source == HandlerParameterBindingSource.Route)
        {
            return "route";
        }

        if (source == HandlerParameterBindingSource.Query)
        {
            return "query";
        }

        if (source == HandlerParameterBindingSource.Header)
        {
            return "header";
        }

        if (source == HandlerParameterBindingSource.Body)
        {
            return "body";
        }

        return source == HandlerParameterBindingSource.Services ? "services" : "parameter";
    }

    private static string GetSingleGenericArgument(string typeFqn, string prefix)
        => typeFqn.Substring(prefix.Length, typeFqn.Length - prefix.Length - 1);

    private static HandlerParameterBinding ResolveExplicitBody(
        HandleParamModel parameter,
        string wireName)
    {
        if (parameter.TypeFqn == "global::System.Threading.CancellationToken")
        {
            return Unsupported(wireName, "CancellationToken cannot be bound from the request body");
        }

        if (parameter.IsInterfaceOrAbstract)
        {
            return Unsupported(wireName, "body parameter has an interface or abstract type");
        }

        return new HandlerParameterBinding(HandlerParameterBindingSource.Body, wireName, null);
    }

    private static HandlerParameterBinding ResolveExplicitScalar(
        HandleParamModel parameter,
        string pattern,
        string wireName,
        HandlerParameterBindingSource source)
    {
        if (!IsSimpleType(parameter.TypeFqn))
        {
            return Unsupported(wireName, $"{BindingSourceName(source)} parameter has unsupported type");
        }

        if (source == HandlerParameterBindingSource.Route && !IsRouteParam(wireName, pattern))
        {
            return Unsupported(wireName, $"route parameter '{wireName}' is not present in the route pattern");
        }

        return new HandlerParameterBinding(source, wireName, null);
    }

    private static HandlerParameterBinding ResolveConventionBinding(
        HandleParamModel parameter,
        string pattern,
        string featureTypeFqn,
        string wireName)
    {
        if (parameter.TypeFqn == featureTypeFqn + ".Request")
        {
            return new HandlerParameterBinding(HandlerParameterBindingSource.Body, wireName, null);
        }

        if (parameter.TypeFqn == "global::System.Threading.CancellationToken")
        {
            return new HandlerParameterBinding(HandlerParameterBindingSource.Services, wireName, null);
        }

        if (!IsSimpleType(parameter.TypeFqn))
        {
            return new HandlerParameterBinding(HandlerParameterBindingSource.Services, wireName, null);
        }

        return IsRouteParam(wireName, pattern)
            ? new HandlerParameterBinding(HandlerParameterBindingSource.Route, wireName, null)
            : new HandlerParameterBinding(HandlerParameterBindingSource.Query, wireName, null);
    }

    private static HandlerParameterBinding Unsupported(string wireName, string reason)
        => new(HandlerParameterBindingSource.Unsupported, wireName, reason);

    private static bool IsSimpleNullableType(string typeFqn)
    {
        if (!typeFqn.StartsWith("global::System.Nullable<", StringComparison.Ordinal))
        {
            return false;
        }

        var inner = typeFqn.Substring("global::System.Nullable<".Length).TrimEnd('>');
        return s_simpleTypes.Contains(inner);
    }

    private static string NormalizeRouteParameterName(string token)
    {
        token = token.TrimStart('*');
        var terminator = token.IndexOfAny([':', '?', '=']);
        return terminator >= 0 ? token.Substring(0, terminator) : token;
    }
}

internal enum HandlerParameterBindingSource
{
    Unsupported,
    Body,
    Route,
    Query,
    Header,
    Services,
}

internal readonly record struct HandlerParameterBinding(
    HandlerParameterBindingSource Source,
    string WireName,
    string? UnsupportedReason);
