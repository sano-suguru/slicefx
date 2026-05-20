using System.Collections.Immutable;

namespace Slice.SourceGenerator;

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
    // Serialised as "typeFqn|name;typeFqn|name" so the record uses primitive equality.
    string SerializedParams,
    // Serialised as "fqn1;fqn2" — order is declaration order.
    string SerializedFilterFqns)
{
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
                // Legacy format (no kind flag) — default to concrete.
                builder.Add(new HandleParamModel(entry.Substring(0, sep), entry.Substring(sep + 1), IsInterfaceOrAbstract: false));
            }
            else
            {
                var typeFqn = entry.Substring(0, sep);
                var name = entry.Substring(sep + 1, sep2 - sep - 1);
                var kind = entry.Substring(sep2 + 1);
                builder.Add(new HandleParamModel(typeFqn, name, IsInterfaceOrAbstract: kind == "I"));
            }
        }
        return builder.ToImmutable();
    }

    public ImmutableArray<string> GetFilterFqns()
    {
        if (string.IsNullOrEmpty(SerializedFilterFqns))
        {
            return [];
        }

        var parts = SerializedFilterFqns.Split(';');
        return ImmutableArray.Create(parts);
    }
}

internal sealed record HandleParamModel(string TypeFqn, string Name, bool IsInterfaceOrAbstract);
