using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
namespace SliceFx.SourceGenerator;

internal static class JsonContextPlanner
{
    public static JsonContextOverrideCandidate CreateOverrideCandidate(GeneratorAttributeSyntaxContext context)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var hasWasiTarget = false;
        var hasLambdaFunctionPerFeatureTarget = false;
        foreach (var attribute in context.Attributes)
        {
            var target = ReadTarget(attribute);
            if (target == JsonContextTarget.Wasi)
            {
                hasWasiTarget = true;
            }
            else if (target == JsonContextTarget.LambdaFunctionPerFeature)
            {
                hasLambdaFunctionPerFeatureTarget = true;
            }
        }

        // Collect all types registered via [JsonSerializable(typeof(T))] on this context type.
        // These form the compile-time body/service discriminator set for WASI and Lambda paths:
        // a request-like param whose type is in this set is classified as a body parameter;
        // otherwise it is resolved from DI (service). This matches ASP.NET Minimal API's runtime
        // IServiceProviderIsService semantics without any per-request reflection.
        var serializableTypes = CollectJsonSerializableTypes(type);

        return new JsonContextOverrideCandidate(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            CreateDiagnosticLocation(type.Locations.Length > 0 ? type.Locations[0] : null),
            InheritsFromJsonSerializerContext(type),
            hasWasiTarget,
            hasLambdaFunctionPerFeatureTarget,
            serializableTypes);
    }

    /// <summary>
    /// Returns a newline-separated, sorted string of raw (global::-prefixed) FQNs for all types
    /// registered via [JsonSerializable(typeof(T))] on <paramref name="contextType"/>.
    /// Uses <see cref="ITypeSymbol"/> (not <see cref="INamedTypeSymbol"/>) to handle array and
    /// constructed generic types (e.g. <c>Dictionary&lt;string, List&lt;int&gt;&gt;</c>).
    /// </summary>
    private static string CollectJsonSerializableTypes(INamedTypeSymbol contextType)
    {
        var fqns = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var attr in contextType.GetAttributes())
        {
            if (!IsJsonSerializableAttribute(attr))
            {
                continue;
            }

            if (attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is ITypeSymbol typeArg)
            {
                fqns.Add(typeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
        }

        return string.Join("\n", fqns);
    }

    private static bool IsJsonSerializableAttribute(AttributeData attr)
    {
        var cls = attr.AttributeClass;
        if (cls is null)
        {
            return false;
        }

        var fqn = cls.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return string.Equals(
            fqn,
            "global::System.Text.Json.Serialization.JsonSerializableAttribute",
            StringComparison.Ordinal);
    }

    public static JsonContextOverrides CreateOverrides(ImmutableArray<JsonContextOverrideCandidate> candidates)
    {
        string? wasi = null;
        string? wasiSerializableTypes = null;
        string? lambda = null;
        string? lambdaSerializableTypes = null;
        var diagnostics = ImmutableArray.CreateBuilder<EquatableDiagnostic>();
        foreach (var candidate in candidates)
        {
            if (!candidate.InheritsFromJsonSerializerContext)
            {
                diagnostics.Add(EquatableDiagnostic.Create(
                    SliceDiagnostics.InvalidJsonContextOverride,
                    candidate.Location,
                    candidate.ContextFqn));
                continue;
            }

            if (candidate.HasWasiTarget)
            {
                if (wasi is not null)
                {
                    diagnostics.Add(DuplicateDiagnostic(JsonContextTarget.Wasi, wasi, candidate));
                }
                else
                {
                    wasi = candidate.ContextFqn;
                    wasiSerializableTypes = candidate.SerializedSerializableTypes;
                }
            }

            if (candidate.HasLambdaFunctionPerFeatureTarget)
            {
                if (lambda is not null)
                {
                    diagnostics.Add(DuplicateDiagnostic(JsonContextTarget.LambdaFunctionPerFeature, lambda, candidate));
                }
                else
                {
                    lambda = candidate.ContextFqn;
                    lambdaSerializableTypes = candidate.SerializedSerializableTypes;
                }
            }
        }

        return new JsonContextOverrides(
            wasi,
            lambda,
            diagnostics.ToImmutable(),
            wasiSerializableTypes ?? "",
            lambdaSerializableTypes ?? "");
    }

    public static JsonContextPlan CreateWasiPlan(
        ImmutableArray<FeatureModel> features,
        string? explicitContextFqn,
        string serializedSerializableTypes = "")
        => CreatePlan(JsonContextTarget.Wasi, features, explicitContextFqn, serializedSerializableTypes);

    public static JsonContextPlan CreateLambdaPlan(
        ImmutableArray<FeatureModel> features,
        string? explicitContextFqn,
        string serializedSerializableTypes = "")
        => CreatePlan(JsonContextTarget.LambdaFunctionPerFeature, features, explicitContextFqn, serializedSerializableTypes);

    public static string StatusForWasi(FeatureModel feature, JsonContextPlan plan)
    {
        var serializableTypes = plan.GetSerializableTypesSet();
        if (GetWasiStructuralSkipReason(feature, serializableTypes) is not null || FindExclusion(plan, feature) is not null)
        {
            return SourceGenerationHelpers.ManifestIneligible;
        }

        return SourceGenerationHelpers.ManifestEligible;
    }

    public static string? ReasonForWasi(FeatureModel feature, JsonContextPlan plan)
    {
        var serializableTypes = plan.GetSerializableTypesSet();
        return GetWasiStructuralSkipReason(feature, serializableTypes) ?? FindExclusion(plan, feature)?.Reason;
    }

    public static string? ReasonForLambda(FeatureModel feature, JsonContextPlan plan)
    {
        var serializableTypes = plan.GetSerializableTypesSet();
        return GetLambdaStructuralSkipReason(feature, serializableTypes) ?? FindExclusion(plan, feature)?.Reason;
    }

    public static FeatureJsonExclusion? FindExclusion(JsonContextPlan plan, FeatureModel feature)
        => plan.FindExclusion(feature.FullyQualifiedTypeName);

    private static string CreateMissingContextReason(JsonContextTarget target, ImmutableArray<JsonRootType> roots)
    {
        var contextName = target == JsonContextTarget.Wasi ? "WASI" : "Lambda function-per-feature";
        var rootList = string.Join(", ", roots.Select(static root => SourceGenerationHelpers.TrimGlobalAlias(root.TypeFqn)));
        return $"{contextName} JSON requires an explicit [SliceJsonContext] JsonSerializerContext with JsonSerializable metadata for: {rootList}";
    }

    public static string? GetWasiStructuralSkipReason(
        FeatureModel feature,
        HashSet<string>? serializableTypes = null)
    {
        if (feature.ReturnsAspNetResult)
        {
            return "IResult is ASP.NET-specific";
        }

        if (feature.RequiresReflectionValidation)
        {
            return "DataAnnotations validation requires reflection";
        }

        return GetParameterBindingSkipReason(feature, serializableTypes);
    }

    public static string? GetLambdaStructuralSkipReason(
        FeatureModel feature,
        HashSet<string>? serializableTypes = null)
    {
        if (feature.ReturnsAspNetResult)
        {
            return "returns ASP.NET IResult";
        }

        if (SourceGenerationHelpers.IsWasiResponseType(SourceGenerationHelpers.GetAwaitedReturnType(feature.ReturnTypeFqn)))
        {
            return "returns SliceFx.Wasi.WasiResponse";
        }

        if (!feature.GetFilterFqns().IsEmpty)
        {
            return "endpoint filters require the ASP.NET endpoint filter pipeline";
        }

        if (feature.RequiresReflectionValidation)
        {
            return "DataAnnotations validation requires reflection in the Lambda function-per-feature path";
        }

        return GetParameterBindingSkipReason(feature, serializableTypes);
    }

    private static JsonContextPlan CreatePlan(
        JsonContextTarget target,
        ImmutableArray<FeatureModel> features,
        string? explicitContextFqn,
        string serializedSerializableTypes = "")
    {
        var exclusions = ImmutableArray.CreateBuilder<FeatureJsonExclusion>();
        var diagnostics = ImmutableArray.CreateBuilder<EquatableDiagnostic>();

        // Parse the serializable-types set once for this plan; used for body/service disambiguation.
        var serializableTypes = ParseSerializableTypes(serializedSerializableTypes);

        foreach (var feature in features)
        {
            if ((target == JsonContextTarget.Wasi
                    ? GetWasiStructuralSkipReason(feature, serializableTypes)
                    : GetLambdaStructuralSkipReason(feature, serializableTypes)) is not null)
            {
                continue;
            }

            var featureRoots = CollectRoots(target, feature, serializableTypes);
            var requiresExplicitContext = target == JsonContextTarget.Wasi;
            var exclusionReason = requiresExplicitContext && explicitContextFqn is null && !featureRoots.IsEmpty
                ? CreateMissingContextReason(target, featureRoots)
                : null;
            exclusionReason ??= ValidateRoots(featureRoots);
            if (exclusionReason is not null)
            {
                var exclusion = new FeatureJsonExclusion(
                    target,
                    feature.FullyQualifiedTypeName,
                    feature.TypeName,
                    feature.EndpointName,
                    feature.GetDiagnosticLocationModel(),
                    exclusionReason);
                exclusions.Add(exclusion);
                diagnostics.Add(EquatableDiagnostic.Create(
                    target == JsonContextTarget.Wasi
                        ? SliceDiagnostics.MissingWasiJsonContext
                        : SliceDiagnostics.MissingLambdaJsonContext,
                    feature.GetDiagnosticLocationModel(),
                    feature.TypeName,
                    exclusionReason));
                continue;
            }

        }

        return new JsonContextPlan(
            target,
            explicitContextFqn,
            exclusions.ToImmutable(),
            diagnostics.ToImmutable(),
            serializedSerializableTypes);
    }

    private static HashSet<string> ParseSerializableTypes(string serialized)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(serialized))
        {
            foreach (var fqn in serialized.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(fqn))
                {
                    set.Add(fqn);
                }
            }
        }

        return set;
    }

    private static ImmutableArray<JsonRootType> CollectRoots(
        JsonContextTarget target,
        FeatureModel feature,
        HashSet<string>? serializableTypes = null)
    {
        var roots = ImmutableArray.CreateBuilder<JsonRootType>();
        var bodyParam = FindBodyParam(feature, serializableTypes);
        if (bodyParam is not null)
        {
            roots.Add(new JsonRootType(bodyParam.TypeFqn));
        }

        var responseType = SourceGenerationHelpers.GetAwaitedReturnType(feature.ReturnTypeFqn);
        if (responseType is not null
            && !IsPassthroughResponseType(target, responseType))
        {
            // Unwrap SliceResult<T> to T so T is the JSON root, not the wrapper struct.
            // Non-generic SliceResult (no body) registers no root, avoiding a spurious SLICE021
            // exclusion for features that only return status codes.
            if (SourceGenerationHelpers.IsSliceResultOfTType(responseType))
            {
                roots.Add(new JsonRootType(SourceGenerationHelpers.GetSliceResultPayloadType(responseType)));
            }
            else if (!SourceGenerationHelpers.IsSliceResultNonGenericType(responseType))
            {
                roots.Add(new JsonRootType(responseType));
            }
        }

        return roots.ToImmutable();
    }

    private static string? ValidateRoots(ImmutableArray<JsonRootType> roots)
    {
        foreach (var root in roots)
        {
            if (root.TypeFqn.Contains("<>", StringComparison.Ordinal)
                || root.TypeFqn.EndsWith("<>", StringComparison.Ordinal)
                || root.TypeFqn.Contains("**", StringComparison.Ordinal)
                || root.TypeFqn.Contains("global::System.Span<", StringComparison.Ordinal)
                || root.TypeFqn.Contains("global::System.ReadOnlySpan<", StringComparison.Ordinal))
            {
                return $"unsupported JSON root type '{SourceGenerationHelpers.TrimGlobalAlias(root.TypeFqn)}'";
            }
        }

        return null;
    }

    private static HandleParamModel? FindBodyParam(
        FeatureModel feature,
        HashSet<string>? serializableTypes = null)
        => SourceGenerationHelpers.FindSingleBodyParameter(feature, serializableTypes);

    private static string? GetParameterBindingSkipReason(
        FeatureModel feature,
        HashSet<string>? serializableTypes = null)
    {
        var bodyCount = 0;
        foreach (var p in feature.GetParams())
        {
            if (p.TypeFqn == "global::System.Threading.CancellationToken")
            {
                continue;
            }

            var binding = SourceGenerationHelpers.ResolveParameterBinding(
                p,
                feature.HttpMethod,
                feature.Pattern,
                serializableTypes);
            if (binding.Source == HandlerParameterBindingSource.Body)
            {
                bodyCount++;
                if (bodyCount > 1)
                {
                    return "multiple body parameters are not supported";
                }
            }

            if (binding.Source == HandlerParameterBindingSource.Unsupported)
            {
                return binding.UnsupportedReason;
            }
        }

        return null;
    }

    private static bool IsPassthroughResponseType(JsonContextTarget target, string responseType)
        => target switch
        {
            JsonContextTarget.Wasi => SourceGenerationHelpers.IsWasiResponseType(responseType),
            JsonContextTarget.LambdaFunctionPerFeature => SourceGenerationHelpers.IsLambdaProxyResponseType(responseType),
            _ => false,
        };

    private static JsonContextTarget? ReadTarget(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length == 0)
        {
            return null;
        }

        return attribute.ConstructorArguments[0].Value switch
        {
            1 => JsonContextTarget.Wasi,
            2 => JsonContextTarget.LambdaFunctionPerFeature,
            _ => null,
        };
    }

    private static EquatableDiagnostic DuplicateDiagnostic(
        JsonContextTarget target,
        string firstContextFqn,
        JsonContextOverrideCandidate secondContext)
        => EquatableDiagnostic.Create(
            SliceDiagnostics.DuplicateJsonContextOverride,
            secondContext.Location,
            target == JsonContextTarget.Wasi ? "Wasi" : "LambdaFunctionPerFeature",
            firstContextFqn,
            secondContext.ContextFqn);

    private static bool InheritsFromJsonSerializerContext(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (string.Equals(
                current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                "global::System.Text.Json.Serialization.JsonSerializerContext",
                StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static DiagnosticLocationModel CreateDiagnosticLocation(Location? location)
    {
        if (location is null || !location.IsInSource)
        {
            return DiagnosticLocationModel.None;
        }

        var lineSpan = location.GetLineSpan();
        return new DiagnosticLocationModel(
            lineSpan.Path,
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            lineSpan.StartLinePosition.Line,
            lineSpan.StartLinePosition.Character,
            lineSpan.EndLinePosition.Line,
            lineSpan.EndLinePosition.Character);
    }
}
