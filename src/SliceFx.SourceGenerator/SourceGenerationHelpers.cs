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

    // Single source of truth for framework-type detection, shared with the CLI. Delegating here
    // (rather than re-implementing) keeps the C# keyword-alias handling (string/int/object/byte/…)
    // consistent across body/request classification and JSON-root detection.
    public static bool IsFrameworkType(string typeFqn)
        => JsonContextRootHelpers.IsFrameworkType(typeFqn);

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
        var body = SelectBodyParameter(feature, knownSerializableTypes).Body;
        return body is null ? [] : [body];
    }

    public static HandleParamModel? FindSingleBodyParameter(
        FeatureModel feature,
        HashSet<string>? knownSerializableTypes = null)
        => SelectBodyParameter(feature, knownSerializableTypes).Body;

    /// <summary>
    /// Single-authority selector for the request body parameter of a feature handler. Applies,
    /// in order: (1) explicit <c>[FromBody]</c> on any verb, (2) a nested type of the feature
    /// class on inferred-body verbs (POST/PUT/PATCH), (3) the sole JSON-context-registered
    /// candidate (or arity fallback when the registered-type set is unknown). Pure function over
    /// <paramref name="feature"/> and <paramref name="knownSerializableTypes"/> — no Roslyn
    /// <c>ISymbol</c> access — to preserve incremental-generator caching.
    /// </summary>
    /// <param name="feature">The feature whose handler parameters are being classified.</param>
    /// <param name="knownSerializableTypes">
    /// The compile-time JSON-context membership set (WASI/Lambda/AOT paths), or <c>null</c> when
    /// the registered-type set is unknown (falls back to arity).
    /// </param>
    /// <returns>
    /// A result where <c>Body</c> is the single unambiguous body parameter, or <c>null</c> when
    /// there is no body or the selection is ambiguous. <c>AmbiguousWith</c> is non-null iff 2+
    /// candidates tie at the same precedence, in which case <c>Body</c> is always <c>null</c> —
    /// callers must check <c>AmbiguousWith</c> before trusting <c>Body</c>.
    /// </returns>
    public static BodySelectionResult SelectBodyParameter(
        FeatureModel feature,
        HashSet<string>? knownSerializableTypes)
    {
        var parameters = feature.GetParams();

        // Precedence 1: explicit [FromBody], on any verb.
        HandleParamModel? explicitBody = null;
        foreach (var p in parameters)
        {
            if (p.BindingSource == "body")
            {
                if (explicitBody is not null)
                {
                    return new BodySelectionResult(null, p); // 2+ [FromBody] → ambiguous
                }

                explicitBody = p;
            }
        }

        if (explicitBody is not null)
        {
            return new BodySelectionResult(explicitBody, null);
        }

        // Precedences 2 & 3 apply only on inferred-body verbs (POST/PUT/PATCH).
        if (!IsInferredBodyMethod(feature.HttpMethod))
        {
            return new BodySelectionResult(null, null);
        }

        // Candidate set: request-like concrete params (IsRequestLikeParameter already excludes
        // route/query/header/services/keyedServices/parameters, CancellationToken, framework,
        // interface/abstract, and simple types).
        var candidates = ImmutableArray.CreateBuilder<HandleParamModel>();
        foreach (var p in parameters)
        {
            if (IsRequestLikeParameter(p))
            {
                candidates.Add(p);
            }
        }

        if (candidates.Count == 0)
        {
            return new BodySelectionResult(null, null);
        }

        // Precedence 2: nested type of the feature class (canonical Request record).
        // Does NOT require JSON-context membership; serializability is enforced downstream.
        HandleParamModel? nested = null;
        foreach (var p in candidates)
        {
            if (JsonContextRootHelpers.IsNestedTypeOf(p.TypeFqn, feature.FullyQualifiedTypeName))
            {
                if (nested is not null)
                {
                    return new BodySelectionResult(null, p); // 2+ nested → ambiguous
                }

                nested = p;
            }
        }

        if (nested is not null)
        {
            return new BodySelectionResult(nested, null);
        }

        // Precedence 3: a body must be serializable. When the set is known, the body is the sole
        // candidate registered in the JSON context; 2+ registered → ambiguous; 0 → all DI.
        if (knownSerializableTypes is not null)
        {
            HandleParamModel? serializable = null;
            foreach (var p in candidates)
            {
                if (knownSerializableTypes.Contains(p.TypeFqn))
                {
                    if (serializable is not null)
                    {
                        return new BodySelectionResult(null, p); // 2+ registered → ambiguous
                    }

                    serializable = p;
                }
            }

            return new BodySelectionResult(serializable, null);
        }

        // Unknown serializable set (e.g. manifest union empty): fall back to arity, matching the
        // pre-change null-path behavior (single candidate = body; multiple = ambiguous).
        return candidates.Count == 1
            ? new BodySelectionResult(candidates[0], null)
            : new BodySelectionResult(null, candidates[1]);
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
        string pattern,
        HandleParamModel? selectedBody = null)
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
            _ => ResolveConventionBinding(parameter, pattern, wireName, selectedBody),
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
        string pattern,
        string wireName,
        HandleParamModel? selectedBody)
    {
        if (parameter.TypeFqn == "global::System.Threading.CancellationToken")
        {
            return new HandlerParameterBinding(HandlerParameterBindingSource.Services, wireName, null);
        }

        if (!IsSimpleType(parameter.TypeFqn))
        {
            // Body is chosen centrally by SelectBodyParameter; here we only reflect it.
            return selectedBody is not null && parameter.Name == selectedBody.Name
                ? new HandlerParameterBinding(HandlerParameterBindingSource.Body, wireName, null)
                : new HandlerParameterBinding(HandlerParameterBindingSource.Services, wireName, null);
        }

        // Route/query branch: unchanged from the original.
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

internal readonly record struct BodySelectionResult(
    HandleParamModel? Body,
    HandleParamModel? AmbiguousWith);
