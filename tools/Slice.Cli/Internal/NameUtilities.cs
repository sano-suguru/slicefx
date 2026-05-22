using System.Text;

namespace Slice.Cli.Internal;

internal static class NameUtilities
{
    private static readonly string[] VerbPrefixes =
    [
        "Create", "Get", "List", "Update", "Delete", "Patch",
        "Find", "Add", "Remove", "Fetch", "Search",
    ];

    private static readonly string[] ResourceQualifierSuffixes =
    [
        "Details", "Detail",
    ];

    internal static string? InferGroup(string featureName)
    {
        foreach (var prefix in VerbPrefixes)
        {
            if (featureName.StartsWith(prefix, StringComparison.Ordinal) && featureName.Length > prefix.Length)
            {
                var remainder = TrimResourceQualifier(featureName[prefix.Length..]);
                // If the remainder is already plural (ends with 's'), return as-is to avoid "Orderses".
                return (remainder.EndsWith('s') || remainder.EndsWith('S')) ? remainder : Pluralize(remainder);
            }
        }
        return null;
    }

    internal static string Pluralize(string singular)
    {
        if (singular.Length == 0)
        {
            return singular;
        }

        if (singular.Length >= 2 &&
            (singular.EndsWith("sh", StringComparison.OrdinalIgnoreCase) ||
             singular.EndsWith("ch", StringComparison.OrdinalIgnoreCase)))
        {
            return singular + "es";
        }

        if (singular.EndsWith('x') || singular.EndsWith('X') ||
            singular.EndsWith('z') || singular.EndsWith('Z'))
        {
            return singular + "es";
        }

        if (singular.EndsWith('s') || singular.EndsWith('S'))
        {
            return singular + "es";
        }

        if ((singular.EndsWith('y') || singular.EndsWith('Y')) && singular.Length >= 2)
        {
            var beforeY = singular[^2];
            if (!"aeiouAEIOU".Contains(beforeY, StringComparison.Ordinal))
            {
                return singular[..^1] + "ies";
            }
        }

        return singular + "s";
    }

    private static string TrimResourceQualifier(string name)
    {
        foreach (var suffix in ResourceQualifierSuffixes)
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }

    internal static string ToKebabCase(string pascal)
    {
        if (string.IsNullOrEmpty(pascal))
        {
            return pascal;
        }

        var sb = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c) && i > 0)
            {
                sb.Append('-');
            }

            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    internal static string ToKebabIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "slice-app";
        }

        var sb = new StringBuilder(value.Length + 4);
        var previousWasSeparator = true;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (char.IsUpper(ch) && i > 0 && !previousWasSeparator)
                {
                    sb.Append('-');
                }

                sb.Append(char.ToLowerInvariant(ch));
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator)
            {
                sb.Append('-');
                previousWasSeparator = true;
            }
        }

        while (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Length--;
        }

        return sb.Length == 0 ? "slice-app" : sb.ToString();
    }

    internal static string ToNamespaceSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "App";
        }

        var sb = new StringBuilder(value.Length + 1);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (i == 0 && !IsIdentifierStart(ch))
            {
                sb.Append('_');
            }

            sb.Append(IsIdentifierPart(ch) ? ch : '_');
        }

        return sb.Length == 0 ? "App" : sb.ToString();
    }

    private static bool IsIdentifierStart(char value)
        => value == '_' || char.IsLetter(value);

    private static bool IsIdentifierPart(char value)
        => value == '_' || char.IsLetterOrDigit(value);
}
