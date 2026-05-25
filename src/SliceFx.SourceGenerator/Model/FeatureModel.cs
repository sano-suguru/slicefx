using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SliceFx.SourceGenerator;

/// <summary>Represents one [Feature] class discovered during source generation.</summary>
internal sealed record FeatureModel(
    string FullyQualifiedTypeName,
    string TypeName,
    string Tag,
    string EndpointName,
    string HttpMethod,
    string Pattern,
    string? Summary,
    string ReturnTypeFqn,
    bool ReturnsAspNetResult,
    // Serialised as "typeFqn|name|kind|nullable|bindingSource|bindingName" so the record uses primitive equality.
    string SerializedParams,
    // Serialised as "fqn1;fqn2" — order is declaration order.
    string SerializedFilterFqns,
    // Serialised as one validation rule per line: "property|rule|arg1|arg2".
    string SerializedValidationRules,
    bool RequiresReflectionValidation,
    // Serialised as "filterFqn|afterFqn;filterFqn|afterFqn" — one entry per FilterOrderHint(After=...) on a filter.
    string SerializedFilterOrderHints,
    string FeatureLocationFilePath,
    int FeatureLocationSourceStart,
    int FeatureLocationSourceLength,
    int FeatureLocationStartLine,
    int FeatureLocationStartCharacter,
    int FeatureLocationEndLine,
    int FeatureLocationEndCharacter)
{
    /// <summary>
    /// Deserializes the feature handler parameters.
    /// </summary>
    /// <returns>The handler parameter models.</returns>
    public ImmutableArray<HandleParamModel> GetParams()
    {
        if (string.IsNullOrEmpty(SerializedParams))
        {
            return [];
        }

        var entries = SerializedParams.Split(';');
        var builder = ImmutableArray.CreateBuilder<HandleParamModel>(entries.Length);
        foreach (var entry in entries)
        {
            var sep = entry.IndexOf('|');
            if (sep < 0)
            {
                continue;
            }

            var sep2 = entry.LastIndexOf('|');
            if (sep2 <= sep)
            {
                // Legacy format (no kind flag) — default to concrete, non-null, and inferred binding.
                builder.Add(new HandleParamModel(
                    entry.Substring(0, sep),
                    entry.Substring(sep + 1),
                    IsInterfaceOrAbstract: false,
                    IsNullable: false,
                    BindingSource: null,
                    BindingName: null));
            }
            else
            {
                var parts = entry.Split('|');
                if (parts.Length < 3)
                {
                    continue;
                }

                builder.Add(new HandleParamModel(
                    parts[0],
                    parts[1],
                    IsInterfaceOrAbstract: parts[2] == "I",
                    IsNullable: parts.Length > 3 && parts[3] == "N",
                    BindingSource: parts.Length > 4 && parts[4].Length > 0 ? parts[4] : null,
                    BindingName: parts.Length > 5 && parts[5].Length > 0 ? parts[5] : null));
            }
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Deserializes the endpoint filter type names referenced by the feature.
    /// </summary>
    /// <returns>The fully qualified endpoint filter type names.</returns>
    public ImmutableArray<string> GetFilterFqns()
    {
        if (string.IsNullOrEmpty(SerializedFilterFqns))
        {
            return [];
        }

        var parts = SerializedFilterFqns.Split(';');
        return ImmutableArray.Create(parts);
    }

    /// <summary>
    /// Deserializes the FilterOrderHint(After=...) pairs collected from filter declarations.
    /// </summary>
    /// <returns>An array of (filter fqn, required-predecessor fqn) tuples.</returns>
    public ImmutableArray<FilterOrderHintEntry> GetFilterOrderHints()
    {
        if (string.IsNullOrEmpty(SerializedFilterOrderHints))
        {
            return [];
        }

        var entries = SerializedFilterOrderHints.Split(';');
        var builder = ImmutableArray.CreateBuilder<FilterOrderHintEntry>(entries.Length);
        foreach (var entry in entries)
        {
            var sep = entry.IndexOf('|');
            if (sep <= 0 || sep >= entry.Length - 1)
            {
                continue;
            }

            builder.Add(new FilterOrderHintEntry(entry.Substring(0, sep), entry.Substring(sep + 1)));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Rehydrates the feature type location used for diagnostics emitted after the incremental model is collected.
    /// </summary>
    /// <returns>The source location when available; otherwise <see cref="Location.None"/>.</returns>
    public Location GetDiagnosticLocation()
        => GetDiagnosticLocationModel().ToLocation();

    /// <summary>
    /// Rehydrates the feature type location model used by cacheable diagnostics.
    /// </summary>
    /// <returns>The source location model when available; otherwise <see cref="DiagnosticLocationModel.None"/>.</returns>
    public DiagnosticLocationModel GetDiagnosticLocationModel()
        => new(
            FeatureLocationFilePath,
            FeatureLocationSourceStart,
            FeatureLocationSourceLength,
            FeatureLocationStartLine,
            FeatureLocationStartCharacter,
            FeatureLocationEndLine,
            FeatureLocationEndCharacter);
}

internal readonly record struct FilterOrderHintEntry(string FilterFqn, string AfterFqn);

internal readonly record struct DiagnosticLocationModel(
    string FilePath,
    int SourceStart,
    int SourceLength,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter)
{
    public static DiagnosticLocationModel None { get; } = new(string.Empty, -1, -1, -1, -1, -1, -1);

    public Location ToLocation()
    {
        if (SourceStart < 0
            || SourceLength < 0)
        {
            return Location.None;
        }

        return Location.Create(
            FilePath,
            new TextSpan(SourceStart, SourceLength),
            new LinePositionSpan(
                new LinePosition(StartLine, StartCharacter),
                new LinePosition(EndLine, EndCharacter)));
    }
}

internal sealed record HandleParamModel(
    string TypeFqn,
    string Name,
    bool IsInterfaceOrAbstract,
    bool IsNullable,
    string? BindingSource,
    string? BindingName);
