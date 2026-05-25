using System.Text;

namespace SliceFx.SourceGenerator;

internal static class GeneratedIdentifier
{
    internal static string FromAssemblyName(string assemblyName, string suffix)
        => Sanitize(assemblyName) + suffix;

    internal static string Sanitize(string value)
    {
        var chars = value.Length == 0 ? "Unknown" : value;
        var sb = new StringBuilder(chars.Length + 1);
        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            if (i == 0 && !IsIdentifierStart(ch))
            {
                sb.Append('_');
            }

            sb.Append(IsIdentifierPart(ch) ? ch : '_');
        }

        return sb.Length == 0 ? "Unknown" : sb.ToString();
    }

    private static bool IsIdentifierStart(char value)
        => value == '_' || char.IsLetter(value);

    private static bool IsIdentifierPart(char value)
        => value == '_' || char.IsLetterOrDigit(value);
}
