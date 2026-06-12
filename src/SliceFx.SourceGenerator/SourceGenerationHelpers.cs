using System.Collections.Immutable;
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
        => parameter.BindingSource is not ("route" or "query" or "header" or "services" or "keyedServices" or "parameters")
           && parameter.TypeFqn != "global::System.Threading.CancellationToken"
           && !parameter.IsInterfaceOrAbstract
           && !IsSimpleType(parameter.TypeFqn)
           && !IsFrameworkType(parameter.TypeFqn);

    public static ImmutableArray<HandleParamModel> FindBodyParameters(
        FeatureModel feature,
        HashSet<string>? knownSerializableTypes = null)
    {
        var parameters = feature.GetParams();
        if (parameters.IsEmpty)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<HandleParamModel>();
        foreach (var parameter in parameters)
        {
            var binding = ResolveParameterBinding(
                parameter,
                feature.HttpMethod,
                feature.Pattern,
                knownSerializableTypes);
            if (binding.Source == HandlerParameterBindingSource.Body)
            {
                builder.Add(parameter);
            }
        }

        return builder.ToImmutable();
    }

    public static HandleParamModel? FindSingleBodyParameter(
        FeatureModel feature,
        HashSet<string>? knownSerializableTypes = null)
    {
        var bodyParameters = FindBodyParameters(feature, knownSerializableTypes);
        return bodyParameters.Length == 1 ? bodyParameters[0] : null;
    }

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
        string httpMethod,
        string pattern,
        HashSet<string>? knownSerializableTypes = null)
    {
        var wireName = string.IsNullOrWhiteSpace(parameter.BindingName) ? parameter.Name : parameter.BindingName!;
        return parameter.BindingSource switch
        {
            "body" => ResolveExplicitBody(parameter, wireName),
            "route" => ResolveExplicitScalar(parameter, pattern, wireName, HandlerParameterBindingSource.Route),
            "query" => ResolveExplicitScalar(parameter, pattern, wireName, HandlerParameterBindingSource.Query),
            "header" => ResolveExplicitScalar(parameter, pattern, wireName, HandlerParameterBindingSource.Header),
            "services" => new HandlerParameterBinding(HandlerParameterBindingSource.Services, wireName, null),
            "keyedServices" => new HandlerParameterBinding(HandlerParameterBindingSource.KeyedServices, wireName, null, parameter.BindingKeyLiteral),
            "parameters" => Unsupported(wireName, "[AsParameters] is not supported in generated WASI/Lambda dispatch; ASP.NET routes are unaffected"),
            _ => ResolveConventionBinding(parameter, httpMethod, pattern, wireName, knownSerializableTypes),
        };
    }

    public static bool IsLambdaProxyResponseType(string? typeFqn)
        => typeFqn == "global::Amazon.Lambda.APIGatewayEvents.APIGatewayHttpApiV2ProxyResponse";

    public static bool IsWasiResponseType(string? typeFqn)
        => typeFqn == "global::SliceFx.Wasi.WasiResponse";

    /// <summary>
    /// Returns <c>true</c> when <paramref name="typeFqn"/> is <c>IResult</c> or a known
    /// concrete IResult type (TypedResults, ObjectResult, etc.). These are pass-throughs
    /// in the ASP.NET NativeAOT path — they execute via <c>IResult.ExecuteAsync</c> and
    /// do not require a <c>JsonTypeInfo</c> root in the context.
    /// </summary>
    public static bool IsAspNetResultType(string? typeFqn)
    {
        if (typeFqn is null)
        {
            return false;
        }

        // Microsoft.AspNetCore.Http.IResult and any type in the Microsoft.AspNetCore.Http.HttpResults
        // namespace (TypedResults.*) are pass-throughs.
        return typeFqn == "global::Microsoft.AspNetCore.Http.IResult"
            || typeFqn.StartsWith("global::Microsoft.AspNetCore.Http.HttpResults.", StringComparison.Ordinal)
            || typeFqn.StartsWith("global::Microsoft.AspNetCore.Mvc.", StringComparison.Ordinal);
    }

    // Prefix for global::SliceFx.SliceResult<T> (generic, arity 1)
    private const string SliceResultOfTPrefix = "global::SliceFx.SliceResult<";

    // Exact FQN for global::SliceFx.SliceResult (non-generic, arity 0)
    private const string SliceResultNonGenericFqn = "global::SliceFx.SliceResult";

    /// <summary>
    /// Returns <c>true</c> when <paramref name="typeFqn"/> is the generic
    /// <c>global::SliceFx.SliceResult&lt;T&gt;</c> (arity 1).
    /// </summary>
    public static bool IsSliceResultOfTType(string? typeFqn)
        => typeFqn is not null
           && typeFqn.StartsWith(SliceResultOfTPrefix, StringComparison.Ordinal)
           && typeFqn.EndsWith(">", StringComparison.Ordinal)
           && !typeFqn.StartsWith(SliceResultNonGenericFqn + ".", StringComparison.Ordinal); // guard sub-types

    /// <summary>
    /// Returns <c>true</c> when <paramref name="typeFqn"/> is the non-generic
    /// <c>global::SliceFx.SliceResult</c> (arity 0).
    /// </summary>
    public static bool IsSliceResultNonGenericType(string? typeFqn)
        => typeFqn == SliceResultNonGenericFqn;

    /// <summary>
    /// Extracts the payload type <c>T</c> from <c>global::SliceFx.SliceResult&lt;T&gt;</c>.
    /// Call only when <see cref="IsSliceResultOfTType"/> returns <c>true</c>.
    /// </summary>
    public static string GetSliceResultPayloadType(string typeFqn)
        => GetSingleGenericArgument(typeFqn, SliceResultOfTPrefix);

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

        if (source == HandlerParameterBindingSource.Services)
        {
            return "services";
        }

        return source == HandlerParameterBindingSource.KeyedServices ? "keyedServices" : "parameter";
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
        string httpMethod,
        string pattern,
        string wireName,
        HashSet<string>? knownSerializableTypes = null)
    {
        if (parameter.TypeFqn == "global::System.Threading.CancellationToken")
        {
            return new HandlerParameterBinding(HandlerParameterBindingSource.Services, wireName, null);
        }

        if (!IsSimpleType(parameter.TypeFqn))
        {
            if (IsInferredBodyMethod(httpMethod) && IsRequestLikeParameter(parameter))
            {
                // When a compile-time JSON-context membership set is provided (WASI/Lambda paths),
                // use it to distinguish body params (registered) from DI services (not registered).
                // This matches ASP.NET Minimal API's runtime IServiceProviderIsService semantics
                // without any per-request reflection. When no set is provided (ASP.NET path), fall
                // back to the original pure-syntax body inference.
                if (knownSerializableTypes is not null)
                {
                    return knownSerializableTypes.Contains(parameter.TypeFqn)
                        ? new HandlerParameterBinding(HandlerParameterBindingSource.Body, wireName, null)
                        : new HandlerParameterBinding(HandlerParameterBindingSource.Services, wireName, null);
                }

                return new HandlerParameterBinding(HandlerParameterBindingSource.Body, wireName, null);
            }

            return new HandlerParameterBinding(HandlerParameterBindingSource.Services, wireName, null);
        }

        return IsRouteParam(wireName, pattern)
            ? new HandlerParameterBinding(HandlerParameterBindingSource.Route, wireName, null)
            : new HandlerParameterBinding(HandlerParameterBindingSource.Query, wireName, null);
    }

    private static HandlerParameterBinding Unsupported(string wireName, string reason)
        => new(HandlerParameterBindingSource.Unsupported, wireName, reason);

    public static bool IsInferredBodyMethod(string httpMethod)
        => httpMethod is "POST" or "PUT" or "PATCH";

    private static bool IsSimpleNullableType(string typeFqn)
    {
        // Long form: global::System.Nullable<global::System.Int32> — kept for completeness.
        if (typeFqn.StartsWith("global::System.Nullable<", StringComparison.Ordinal))
        {
            var inner = typeFqn.Substring("global::System.Nullable<".Length).TrimEnd('>');
            return s_simpleTypes.Contains(inner);
        }

        // Trailing-? form: what SymbolDisplayFormat.FullyQualifiedFormat actually emits for
        // nullable value types (e.g. int? → "int?", Guid? → "global::System.Guid?").
        // string? and other reference types are NOT emitted with a trailing ? by FullyQualifiedFormat,
        // so this branch only fires for genuine Nullable<T> value types.
        if (typeFqn.EndsWith("?", StringComparison.Ordinal))
        {
            return s_simpleTypes.Contains(typeFqn.Substring(0, typeFqn.Length - 1));
        }

        return false;
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
    KeyedServices,
}

internal readonly record struct HandlerParameterBinding(
    HandlerParameterBindingSource Source,
    string WireName,
    string? UnsupportedReason,
    string? KeyLiteral = null);
