using System.Collections.Immutable;

namespace Slice.SourceGenerator;

internal sealed class JsonContextPlan : IEquatable<JsonContextPlan>
{
    private readonly Dictionary<string, FeatureJsonExclusion> _exclusionsByFeature;

    public JsonContextPlan(
        JsonContextTarget target,
        string? contextFqn,
        ImmutableArray<FeatureJsonExclusion> exclusions,
        ImmutableArray<EquatableDiagnostic> diagnostics)
    {
        Target = target;
        ContextFqn = contextFqn;
        Exclusions = exclusions;
        Diagnostics = diagnostics;
        _exclusionsByFeature = new Dictionary<string, FeatureJsonExclusion>(StringComparer.Ordinal);
        foreach (var exclusion in exclusions)
        {
            _exclusionsByFeature[exclusion.FeatureTypeFqn] = exclusion;
        }
    }

    public JsonContextTarget Target { get; }

    public string? ContextFqn { get; }

    public ImmutableArray<FeatureJsonExclusion> Exclusions { get; }

    public ImmutableArray<EquatableDiagnostic> Diagnostics { get; }

    public FeatureJsonExclusion? FindExclusion(string featureTypeFqn)
        => _exclusionsByFeature.TryGetValue(featureTypeFqn, out var exclusion)
            ? exclusion
            : null;

    public bool Equals(JsonContextPlan? other)
        => other is not null
           && Target == other.Target
           && string.Equals(ContextFqn, other.ContextFqn, StringComparison.Ordinal)
           && Exclusions.SequenceEqual(other.Exclusions)
           && Diagnostics.SequenceEqual(other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as JsonContextPlan);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)Target;
            hash = (hash * 31) + (ContextFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(ContextFqn));
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
    LambdaPerFeature,
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
    bool HasLambdaPerFeatureTarget);

internal sealed class JsonContextOverrides : IEquatable<JsonContextOverrides>
{
    public JsonContextOverrides(
        string? wasiContextFqn,
        string? lambdaPerFeatureContextFqn,
        ImmutableArray<EquatableDiagnostic> diagnostics)
    {
        WasiContextFqn = wasiContextFqn;
        LambdaPerFeatureContextFqn = lambdaPerFeatureContextFqn;
        Diagnostics = diagnostics;
    }

    public string? WasiContextFqn { get; }

    public string? LambdaPerFeatureContextFqn { get; }

    public ImmutableArray<EquatableDiagnostic> Diagnostics { get; }

    public bool Equals(JsonContextOverrides? other)
        => other is not null
           && string.Equals(WasiContextFqn, other.WasiContextFqn, StringComparison.Ordinal)
           && string.Equals(LambdaPerFeatureContextFqn, other.LambdaPerFeatureContextFqn, StringComparison.Ordinal)
           && Diagnostics.SequenceEqual(other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as JsonContextOverrides);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = WasiContextFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(WasiContextFqn);
            hash = (hash * 31) + (LambdaPerFeatureContextFqn is null ? 0 : StringComparer.Ordinal.GetHashCode(LambdaPerFeatureContextFqn));
            foreach (var diagnostic in Diagnostics)
            {
                hash = (hash * 31) + diagnostic.GetHashCode();
            }

            return hash;
        }
    }
}
