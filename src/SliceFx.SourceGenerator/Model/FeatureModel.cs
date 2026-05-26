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
    // Serialised as "b64(typeFqn)|b64(name)|kind|nullable|b64(bindingSource)|b64(bindingName)|valueKind[|b64(bindingKeyLiteral)]" so the record uses primitive equality.
    string SerializedParams,
    // Serialised as "fqn1;fqn2" — order is declaration order.
    string SerializedFilterFqns,
    // Serialised as one validation rule per line: "parameterIndex|parameterType|property|rule|arg1|arg2".
    string SerializedValidationRules,
    bool RequiresReflectionValidation,
    // Serialised as one unsupported validation attribute per line.
    string SerializedUnsupportedValidationAttributes,
    // Serialised as "filterFqn|afterFqn;filterFqn|afterFqn" — one entry per FilterOrderHint(After=...) on a filter.
    string SerializedFilterOrderHints,
    string? LambdaStartupTypeFqn,
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
            var parts = entry.Split('|');
            if (parts.Length < 7)
            {
                continue;
            }

            builder.Add(new HandleParamModel(
                Decode(parts[0]),
                Decode(parts[1]),
                IsInterfaceOrAbstract: parts[2] == "I",
                IsNullable: parts[3] == "N",
                IsValueType: parts[6] == "V",
                BindingSource: parts[4].Length > 0 ? Decode(parts[4]) : null,
                BindingName: parts[5].Length > 0 ? Decode(parts[5]) : null,
                BindingKeyLiteral: parts.Length > 7 && parts[7].Length > 0 ? Decode(parts[7]) : null));
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
    /// Deserializes generated validation rules grouped by handler parameter.
    /// </summary>
    /// <returns>The generated validation parameter models.</returns>
    public ImmutableArray<ValidationParameterModel> GetValidationParameters()
    {
        if (string.IsNullOrEmpty(SerializedValidationRules))
        {
            return [];
        }

        var groups = new Dictionary<(int Index, string TypeFqn), List<string>>();
        foreach (var line in SerializedValidationRules.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length < 4 || !int.TryParse(parts[0], out var index))
            {
                continue;
            }

            var key = (index, parts[1]);
            if (!groups.TryGetValue(key, out var rules))
            {
                rules = [];
                groups.Add(key, rules);
            }

            rules.Add(string.Join("|", parts.Skip(2)));
        }

        var builder = ImmutableArray.CreateBuilder<ValidationParameterModel>(groups.Count);
        foreach (var group in groups.OrderBy(static group => group.Key.Index))
        {
            builder.Add(new ValidationParameterModel(
                group.Key.Index,
                group.Key.TypeFqn,
                string.Join("\n", group.Value)));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Deserializes unsupported validation attributes for diagnostics.
    /// </summary>
    /// <returns>The unsupported validation attribute models.</returns>
    public ImmutableArray<UnsupportedValidationAttributeModel> GetUnsupportedValidationAttributes()
    {
        if (string.IsNullOrEmpty(SerializedUnsupportedValidationAttributes))
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<UnsupportedValidationAttributeModel>();
        foreach (var line in SerializedUnsupportedValidationAttributes.Split('\n'))
        {
            var parts = line.Split('|');
            if (parts.Length != 9
                || !int.TryParse(parts[1], out var sourceStart)
                || !int.TryParse(parts[2], out var sourceLength)
                || !int.TryParse(parts[3], out var startLine)
                || !int.TryParse(parts[4], out var startCharacter)
                || !int.TryParse(parts[5], out var endLine)
                || !int.TryParse(parts[6], out var endCharacter))
            {
                continue;
            }

            builder.Add(new UnsupportedValidationAttributeModel(
                Decode(parts[0]),
                sourceStart,
                sourceLength,
                startLine,
                startCharacter,
                endLine,
                endCharacter,
                Decode(parts[7]),
                Decode(parts[8])));
        }

        return builder.ToImmutable();
    }

    private static string Decode(string value)
        => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));

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

internal readonly record struct ValidationParameterModel(
    int Index,
    string TypeFqn,
    string SerializedRules);

internal readonly record struct UnsupportedValidationAttributeModel(
    string FilePath,
    int SourceStart,
    int SourceLength,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter,
    string FeatureName,
    string AttributeName)
{
    public DiagnosticLocationModel GetDiagnosticLocationModel()
        => new(
            FilePath,
            SourceStart,
            SourceLength,
            StartLine,
            StartCharacter,
            EndLine,
            EndCharacter);
}

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
    bool IsValueType,
    string? BindingSource,
    string? BindingName,
    string? BindingKeyLiteral = null);
