namespace Slice.Cli.Internal;

internal static class CliValidation
{
    private static readonly HashSet<string> CSharpKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach",
        "goto", "if", "implicit", "in", "int", "interface", "internal",
        "is", "lock", "long", "namespace", "new", "null", "object",
        "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void",
        "volatile", "while",
    ];

    private static readonly HashSet<string> SupportedMethods =
    [
        "GET", "POST", "PUT", "DELETE", "PATCH",
    ];

    internal static string NormalizeHttpMethod(string method)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new CliException("HTTP method is required.");
        }

        var normalized = method.ToUpperInvariant();
        return !SupportedMethods.Contains(normalized)
            ? throw new CliException($"Unsupported HTTP method '{method}'. Supported methods: {string.Join(", ", SupportedMethods)}.")
            : normalized;
    }

    internal static string RequireClassName(string value, string argumentName)
    {
        var name = RequireIdentifier(value, argumentName);
        return !char.IsUpper(name[0]) ? throw new CliException($"{argumentName} must start with an uppercase letter.") : name;
    }

    internal static string[] RequireGroupSegments(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliException("Group is required.");
        }

        var group = RequireNoSurroundingWhitespace(value, "Group");
        if (group.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
            group.Contains(Path.DirectorySeparatorChar) ||
            group.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new CliException("Group must use namespace-style segments, for example 'Users' or 'Admin.Users'.");
        }

        var segments = group.Split('.');
        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new CliException("Group must not contain empty namespace segments.");
        }

        foreach (var segment in segments)
        {
            RequireClassName(segment, "Group segment");
        }

        return segments;
    }

    internal static string RequireNamespace(string value, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliException($"{sourceName} is required.");
        }

        var normalized = RequireNoSurroundingWhitespace(value, sourceName);
        var segments = normalized.Split('.');
        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new CliException($"{sourceName} must not contain empty namespace segments.");
        }

        foreach (var segment in segments)
        {
            RequireIdentifier(segment, $"{sourceName} segment");
        }

        return normalized;
    }

    internal static string NormalizeRoute(string? route, string featureName)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "/" + NameUtilities.ToKebabCase(featureName);
        }

        var normalized = RequireNoSurroundingWhitespace(route, "Route");
        return normalized.Any(char.IsControl)
            ? throw new CliException("Route must not contain control characters.")
            : normalized.StartsWith('/') ? normalized : "/" + normalized;
    }

    internal static string RequireKebabIdentifier(string value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliException($"{argumentName} is required.");
        }

        var identifier = RequireNoSurroundingWhitespace(value, argumentName);
        if (identifier.Length == 0
            || identifier[0] is '-'
            || identifier[^1] is '-'
            || identifier.Any(static ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-')))
        {
            throw new CliException($"{argumentName} must contain only ASCII letters, digits, and hyphens, and must not start or end with a hyphen.");
        }

        return identifier.ToLowerInvariant();
    }

    private static string RequireIdentifier(string value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliException($"{argumentName} is required.");
        }

        var identifier = RequireNoSurroundingWhitespace(value, argumentName);
        return !IsIdentifier(identifier) || CSharpKeywords.Contains(identifier)
            ? throw new CliException($"{argumentName} must be a valid C# identifier.")
            : identifier;
    }

    private static string RequireNoSurroundingWhitespace(string value, string argumentName)
    {
        var trimmed = value.Trim();
        return trimmed.Length != value.Length
            ? throw new CliException($"{argumentName} must not contain leading or trailing whitespace.")
            : trimmed;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !IsIdentifierStart(value[0]))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!IsIdentifierPart(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char value)
        => value == '_' || char.IsLetter(value);

    private static bool IsIdentifierPart(char value)
        => value == '_' || char.IsLetterOrDigit(value);
}
