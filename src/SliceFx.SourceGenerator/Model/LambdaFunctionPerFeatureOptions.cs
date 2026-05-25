using System.Collections.Immutable;

namespace SliceFx.SourceGenerator;

internal sealed class LambdaFunctionPerFeatureOptions : IEquatable<LambdaFunctionPerFeatureOptions>
{
    public LambdaFunctionPerFeatureOptions(
        bool enabled,
        string? startupTypeFqn,
        ImmutableArray<EquatableDiagnostic> diagnostics)
    {
        Enabled = enabled;
        StartupTypeFqn = startupTypeFqn;
        Diagnostics = diagnostics;
    }

    public bool Enabled { get; }

    public string? StartupTypeFqn { get; }

    public ImmutableArray<EquatableDiagnostic> Diagnostics { get; }

    public bool Equals(LambdaFunctionPerFeatureOptions? other)
        => other is not null
           && Enabled == other.Enabled
           && string.Equals(StartupTypeFqn, other.StartupTypeFqn, StringComparison.Ordinal)
           && Diagnostics.SequenceEqual(other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as LambdaFunctionPerFeatureOptions);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Enabled ? 17 : 23;
            hash = (hash * 31) + (StartupTypeFqn?.GetHashCode() ?? 0);
            foreach (var diagnostic in Diagnostics)
            {
                hash = (hash * 31) + diagnostic.GetHashCode();
            }

            return hash;
        }
    }
}
