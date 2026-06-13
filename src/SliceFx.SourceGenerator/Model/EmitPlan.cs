using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SliceFx.SourceGenerator;

internal sealed class EmitPlan : IEquatable<EmitPlan>
{
    public EmitPlan(
        ImmutableArray<GeneratedSource> sources,
        ImmutableArray<EquatableDiagnostic> diagnostics)
    {
        Sources = sources;
        Diagnostics = diagnostics;
    }

    public ImmutableArray<GeneratedSource> Sources { get; }

    public ImmutableArray<EquatableDiagnostic> Diagnostics { get; }

    public bool Equals(EmitPlan? other)
        => other is not null
           && Sources.SequenceEqual(other.Sources)
           && Diagnostics.SequenceEqual(other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as EmitPlan);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var source in Sources)
            {
                hash = (hash * 31) + source.GetHashCode();
            }

            foreach (var diagnostic in Diagnostics)
            {
                hash = (hash * 31) + diagnostic.GetHashCode();
            }

            return hash;
        }
    }
}

internal readonly struct GeneratedSource : IEquatable<GeneratedSource>
{
    private readonly int _sourceHash;
    private readonly int _sourceLength;

    public GeneratedSource(string hintName, string source)
    {
        HintName = hintName;
        Source = source;
        _sourceHash = StringComparer.Ordinal.GetHashCode(source);
        _sourceLength = source.Length;
    }

    public string HintName { get; }

    public string Source { get; }

    public bool Equals(GeneratedSource other)
        => string.Equals(HintName, other.HintName, StringComparison.Ordinal)
           && _sourceLength == other._sourceLength
           && _sourceHash == other._sourceHash
           && string.Equals(Source, other.Source, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is GeneratedSource other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.Ordinal.GetHashCode(HintName);
            hash = (hash * 31) + _sourceLength;
            hash = (hash * 31) + _sourceHash;
            return hash;
        }
    }
}

internal readonly struct EquatableDiagnostic : IEquatable<EquatableDiagnostic>
{
    private EquatableDiagnostic(
        DiagnosticDescriptor descriptor,
        DiagnosticLocationModel location,
        ImmutableArray<string?> arguments,
        ImmutableArray<KeyValuePair<string, string>> properties)
    {
        Descriptor = descriptor;
        Location = location;
        Arguments = arguments;
        Properties = properties;
    }

    public DiagnosticDescriptor Descriptor { get; }

    public DiagnosticLocationModel Location { get; }

    public ImmutableArray<string?> Arguments { get; }

    /// <summary>
    /// Structured data for code fixes (e.g. MissingRoots, Target, ContextFqn).
    /// Must participate in Equals/GetHashCode to keep incremental caching correct.
    /// </summary>
    public ImmutableArray<KeyValuePair<string, string>> Properties { get; }

    public static EquatableDiagnostic Create(
        DiagnosticDescriptor descriptor,
        DiagnosticLocationModel location,
        params string?[] arguments)
        => new(
            descriptor,
            location,
            arguments.Length == 0 ? [] : [.. arguments],
            []);

    public static EquatableDiagnostic CreateWithProperties(
        DiagnosticDescriptor descriptor,
        DiagnosticLocationModel location,
        IReadOnlyList<KeyValuePair<string, string>> properties,
        params string?[] arguments)
        => new(
            descriptor,
            location,
            arguments.Length == 0 ? [] : [.. arguments],
            [.. properties]);

    public Diagnostic ToDiagnostic()
    {
        if (Properties.IsDefaultOrEmpty)
        {
            return Diagnostic.Create(Descriptor, Location.ToLocation(), GetArguments());
        }

        var builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        foreach (var kvp in Properties)
        {
            builder.Add(kvp.Key, kvp.Value);
        }

        return Diagnostic.Create(Descriptor, Location.ToLocation(), builder.ToImmutable(), GetArguments());
    }

    public bool Equals(EquatableDiagnostic other)
        => EqualityComparer<DiagnosticDescriptor>.Default.Equals(Descriptor, other.Descriptor)
           && Location.Equals(other.Location)
           && ArgumentsEqual(Arguments, other.Arguments)
           && PropertiesEqual(Properties, other.Properties);

    public override bool Equals(object? obj) => obj is EquatableDiagnostic other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Descriptor?.GetHashCode() ?? 0;
            hash = (hash * 31) + Location.GetHashCode();
            if (!Arguments.IsDefaultOrEmpty)
            {
                foreach (var argument in Arguments)
                {
                    hash = (hash * 31) + (argument is null ? 0 : StringComparer.Ordinal.GetHashCode(argument));
                }
            }

            if (!Properties.IsDefaultOrEmpty)
            {
                foreach (var kvp in Properties)
                {
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(kvp.Key);
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(kvp.Value);
                }
            }

            return hash;
        }
    }

    private static bool ArgumentsEqual(ImmutableArray<string?> left, ImmutableArray<string?> right)
    {
        if (left.IsDefaultOrEmpty || right.IsDefaultOrEmpty)
        {
            return left.IsDefaultOrEmpty && right.IsDefaultOrEmpty;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PropertiesEqual(
        ImmutableArray<KeyValuePair<string, string>> left,
        ImmutableArray<KeyValuePair<string, string>> right)
    {
        if (left.IsDefaultOrEmpty || right.IsDefaultOrEmpty)
        {
            return left.IsDefaultOrEmpty && right.IsDefaultOrEmpty;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i].Key, right[i].Key, StringComparison.Ordinal)
                || !string.Equals(left[i].Value, right[i].Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private object[] GetArguments()
    {
        if (Arguments.IsDefaultOrEmpty)
        {
            return [];
        }

        var result = new object[Arguments.Length];
        for (var i = 0; i < Arguments.Length; i++)
        {
            result[i] = Arguments[i] ?? string.Empty;
        }

        return result;
    }
}
