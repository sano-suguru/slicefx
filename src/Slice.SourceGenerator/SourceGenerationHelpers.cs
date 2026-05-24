namespace Slice.SourceGenerator;

internal static class SourceGenerationHelpers
{
    public const string SliceValidatorFilterPrefix = "global::Slice.SliceValidatorFilter<";
    public const string ManifestSchemaVersion = "2";
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

    private static string GetSingleGenericArgument(string typeFqn, string prefix)
        => typeFqn.Substring(prefix.Length, typeFqn.Length - prefix.Length - 1);

    private static bool IsSimpleNullableType(string typeFqn)
    {
        if (!typeFqn.StartsWith("global::System.Nullable<", StringComparison.Ordinal))
        {
            return false;
        }

        var inner = typeFqn.Substring("global::System.Nullable<".Length).TrimEnd('>');
        return s_simpleTypes.Contains(inner);
    }

    private static string NormalizeRouteParameterName(string token)
    {
        token = token.TrimStart('*');
        var terminator = token.IndexOfAny([':', '?', '=']);
        return terminator >= 0 ? token.Substring(0, terminator) : token;
    }
}
