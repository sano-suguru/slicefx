using System.Collections.Immutable;

namespace SliceFx.SourceGenerator;

internal sealed class JsonContextPlan : IEquatable<JsonContextPlan>
{
    private readonly Dictionary<string, FeatureJsonExclusion> _exclusionsByFeature;
    private HashSet<string>? _serializableTypesSet;

    public JsonContextPlan(
        JsonContextTarget target,
        string? contextFqn,
        ImmutableArray<FeatureJsonExclusion> exclusions,
        ImmutableArray<EquatableDiagnostic> diagnostics,
        string serializableTypes = "")
    {
        Target = target;
        ContextFqn = contextFqn;
        Exclusions = exclusions;
        Diagnostics = diagnostics;
        SerializableTypes = serializableTypes;
        _exclusionsByFeature = new Dictionary<string, FeatureJsonExclusion>(StringComparer.Ordinal);
        foreach (var exclusion in exclusions)
        {
            _exclusionsByFeature[exclusion.FeatureTypeFqn] = exclusion;
        }
    }

    public JsonContextTarget Target { get; }

    public string? ContextFqn { get; }

    /// <summary>
    /// Newline-separated, sorted, raw (global::-prefixed) FQNs of types registered via
    /// [JsonSerializable(typeof(T))] in the associated JSON context.
    /// Empty string when no context is present or the context has no [JsonSerializable] entries.
    /// Used as the compile-time body/service discriminator for WASI and Lambda paths.
    /// Stored as a string for value equality in the incremental pipeline.
    /// </summary>
    public string SerializableTypes { get; }

    public ImmutableArray<FeatureJsonExclusion> Exclusions { get; }

    public ImmutableArray<EquatableDiagnostic> Diagnostics { get; }

    public FeatureJsonExclusion? FindExclusion(string featureTypeFqn)
        => _exclusionsByFeature.TryGetValue(featureTypeFqn, out var exclusion)
            ? exclusion
            : null;

    /// <summary>
    /// Returns the parsed set of serializable type FQNs (raw, global::-prefixed).
    /// Parsed lazily and cached; the result is stable for the lifetime of this plan instance.
    /// </summary>
    public HashSet<string> GetSerializableTypesSet()
    {
        if (_serializableTypesSet is not null)
        {
            return _serializableTypesSet;
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(SerializableTypes))
        {
            foreach (var fqn in SerializableTypes.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(fqn))
                {
                    set.Add(fqn);
                }
            }
        }

        return _serializableTypesSet = set;
    }

    public bool Equals(JsonContextPlan? other)
        => other is not null
           && Target == other.Target
           && string.Equals(ContextFqn, other.ContextFqn, StringComparison.Ordinal)
           && string.Equals(SerializableTypes, other.SerializableTypes, StringComparison.Ordinal)
           && Exclusions.SequenceEqual(other.Exclusions)
           && Diagnostics.SequenceEqual(other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as JsonContextPlan);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)Target;
            hash = (hash * 31) + (ContextFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(ContextFqn));
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(SerializableTypes);
            foreach (var exclusion in Exclusions)
            {
                hash = (hash * 31) + exclusion.GetHashCode();
            }

            foreach (var diagnostic in Diagnostics)
            {
                hash = (hash * 31) + diagnostic.GetHashCode();
            }

            return hash;
        }
    }
}

internal enum JsonContextTarget
{
    Wasi,
    LambdaFunctionPerFeature,
    AspNet,
}

internal readonly record struct JsonRootType(string TypeFqn);

internal readonly record struct FeatureJsonExclusion(
    JsonContextTarget Target,
    string FeatureTypeFqn,
    string FeatureTypeName,
    string EndpointName,
    DiagnosticLocationModel Location,
    string Reason);

internal readonly record struct JsonContextOverrideCandidate(
    string ContextFqn,
    DiagnosticLocationModel Location,
    bool InheritsFromJsonSerializerContext,
    bool HasWasiTarget,
    bool HasLambdaFunctionPerFeatureTarget,
    bool HasAspNetTarget,
    // Newline-separated, sorted, raw (global::-prefixed) FQNs of types registered via
    // [JsonSerializable(typeof(T))] in this context. Empty string if none.
    string SerializedSerializableTypes);

internal sealed class JsonContextOverrides : IEquatable<JsonContextOverrides>
{
    public JsonContextOverrides(
        string? wasiContextFqn,
        string? lambdaFunctionPerFeatureContextFqn,
        string? aspNetContextFqn,
        ImmutableArray<EquatableDiagnostic> diagnostics,
        string wasiSerializableTypes = "",
        string lambdaSerializableTypes = "",
        string aspNetSerializableTypes = "")
    {
        WasiContextFqn = wasiContextFqn;
        LambdaFunctionPerFeatureContextFqn = lambdaFunctionPerFeatureContextFqn;
        AspNetContextFqn = aspNetContextFqn;
        Diagnostics = diagnostics;
        WasiSerializableTypes = wasiSerializableTypes;
        LambdaSerializableTypes = lambdaSerializableTypes;
        AspNetSerializableTypes = aspNetSerializableTypes;
    }

    public string? WasiContextFqn { get; }

    public string? LambdaFunctionPerFeatureContextFqn { get; }

    /// <summary>
    /// FQN of the [SliceJsonContext(SliceJsonTarget.AspNet)] JsonSerializerContext, or null if none.
    /// </summary>
    public string? AspNetContextFqn { get; }

    /// <summary>
    /// Newline-separated, sorted, raw FQNs of types in the [SliceJsonContext(Wasi)] context.
    /// Empty string if no Wasi context was found.
    /// </summary>
    public string WasiSerializableTypes { get; }

    /// <summary>
    /// Newline-separated, sorted, raw FQNs of types in the [SliceJsonContext(LambdaFunctionPerFeature)] context.
    /// Empty string if no Lambda context was found.
    /// </summary>
    public string LambdaSerializableTypes { get; }

    /// <summary>
    /// Newline-separated, sorted, raw FQNs of types in the [SliceJsonContext(AspNet)] context.
    /// Empty string if no AspNet context was found.
    /// </summary>
    public string AspNetSerializableTypes { get; }

    public ImmutableArray<EquatableDiagnostic> Diagnostics { get; }

    public bool Equals(JsonContextOverrides? other)
        => other is not null
           && string.Equals(WasiContextFqn, other.WasiContextFqn, StringComparison.Ordinal)
           && string.Equals(LambdaFunctionPerFeatureContextFqn, other.LambdaFunctionPerFeatureContextFqn, StringComparison.Ordinal)
           && string.Equals(AspNetContextFqn, other.AspNetContextFqn, StringComparison.Ordinal)
           && string.Equals(WasiSerializableTypes, other.WasiSerializableTypes, StringComparison.Ordinal)
           && string.Equals(LambdaSerializableTypes, other.LambdaSerializableTypes, StringComparison.Ordinal)
           && string.Equals(AspNetSerializableTypes, other.AspNetSerializableTypes, StringComparison.Ordinal)
           && Diagnostics.SequenceEqual(other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as JsonContextOverrides);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = WasiContextFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(WasiContextFqn);
            hash = (hash * 31) + (LambdaFunctionPerFeatureContextFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(LambdaFunctionPerFeatureContextFqn));
            hash = (hash * 31) + (AspNetContextFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(AspNetContextFqn));
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(WasiSerializableTypes);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(LambdaSerializableTypes);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(AspNetSerializableTypes);
            foreach (var diagnostic in Diagnostics)
            {
                hash = (hash * 31) + diagnostic.GetHashCode();
            }

            return hash;
        }
    }
}
