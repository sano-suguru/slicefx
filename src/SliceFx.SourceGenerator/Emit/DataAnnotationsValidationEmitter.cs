using System.Globalization;
using System.Text;

namespace SliceFx.SourceGenerator;

internal static class DataAnnotationsValidationEmitter
{
    public static void EmitValidationRule(StringBuilder sb, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var parts = line.Split('|');
        if (parts.Length < 2)
        {
            return;
        }

        var propertyName = parts[0];
        var propertyAccess = $"value.{propertyName}";
        var localName = "__" + SanitizeIdentifier(propertyName);
        if (parts[1] == "Required")
        {
            var kind = parts.Length > 2 ? parts[2] : "String";
            var allowEmptyStrings = parts.Length > 3 && parts[3] == "true";
            var message = DecodeValidationMessage(parts, 4, $"The {propertyName} field is required.");
            if (kind == "String" && !allowEmptyStrings)
            {
                sb.AppendLine($"        if ({propertyAccess} is null || ({propertyAccess} is string {localName}Required && {localName}Required.Length == 0))");
            }
            else
            {
                sb.AppendLine($"        if ({propertyAccess} is null)");
            }

            EmitAddValidationError(sb, propertyName, message);
        }
        else if (parts[1] == "StringLength" && parts.Length > 3)
        {
            var message = DecodeValidationMessage(parts, 4, $"The field {propertyName} must be a string with a minimum length of {parts[2]} and a maximum length of {parts[3]}.");
            sb.AppendLine($"        if ({propertyAccess} is string {localName}StringLength && ({localName}StringLength.Length < {parts[2]} || {localName}StringLength.Length > {parts[3]}))");
            EmitAddValidationError(sb, propertyName, message);
        }
        else if ((parts[1] == "MinLength" || parts[1] == "MaxLength") && parts.Length > 3)
        {
            var comparison = parts[1] == "MinLength" ? "<" : ">";
            var message = DecodeValidationMessage(parts, 4, parts[1] == "MinLength"
                ? $"The field {propertyName} must be a string or array type with a minimum length of '{parts[2]}'."
                : $"The field {propertyName} must be a string or array type with a maximum length of '{parts[2]}'.");
            sb.AppendLine($"        if ({propertyAccess} is not null && {propertyAccess}.{parts[3]} {comparison} {parts[2]})");
            EmitAddValidationError(sb, propertyName, message);
        }
        else if (parts[1] == "EmailAddress")
        {
            var message = DecodeValidationMessage(parts, 2, $"The {propertyName} field is not a valid e-mail address.");
            sb.AppendLine($"        if ({propertyAccess} is string {localName}Email && !s_emailAddressAttribute.IsValid({localName}Email))");
            EmitAddValidationError(sb, propertyName, message);
        }
        else if (parts[1] == "Url")
        {
            var message = DecodeValidationMessage(parts, 2, $"The {propertyName} field is not a valid fully-qualified http, https, or ftp URL.");
            sb.AppendLine($"        if ({propertyAccess} is string {localName}Url && !s_urlAttribute.IsValid({localName}Url))");
            EmitAddValidationError(sb, propertyName, message);
        }
        else if (parts[1] == "HttpsUrl")
        {
            var message = DecodeValidationMessage(parts, 2, $"The {propertyName} field is not a valid HTTPS URL.");
            // Emit a compile-time Uri.TryCreate + scheme check — no stored attribute instance needed.
            sb.AppendLine($"        if ({propertyAccess} is string {localName}HttpsUrl && !(global::System.Uri.TryCreate({localName}HttpsUrl, global::System.UriKind.Absolute, out var {localName}HttpsUrlParsed) && {localName}HttpsUrlParsed.Scheme == \"https\"))");
            EmitAddValidationError(sb, propertyName, message);
        }
        else if (parts[1] == "RegularExpression" && parts.Length > 3)
        {
            var pattern = DecodeValidationMessage(parts, 2, "");
            var message = DecodeValidationMessage(parts, 3, $"The field {propertyName} must match the regular expression '{pattern}'.");
            var matchTimeoutInMilliseconds = GetRegularExpressionMatchTimeout(parts);
            var fieldName = RegularExpressionAttributeFieldName(parts[2], matchTimeoutInMilliseconds);
            sb.AppendLine($"        if (!{fieldName}.IsValid({propertyAccess}))");
            EmitAddValidationError(sb, propertyName, message);
        }
        else if (parts[1] == "Range" && parts.Length > 5)
        {
            var message = DecodeValidationMessage(parts, 5, $"The field {propertyName} must be between {parts[3]} and {parts[4]}.");
            sb.AppendLine($"        if ({propertyAccess} is {parts[2]} {localName}Range && ({localName}Range < {parts[3]} || {localName}Range > {parts[4]}))");
            EmitAddValidationError(sb, propertyName, message);
        }
    }

    public static void EmitValidationAttributeFields(StringBuilder sb, IEnumerable<ValidationParameterModel> parameters)
    {
        var rules = parameters
            .SelectMany(static parameter => parameter.SerializedRules.Split('\n'))
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var hasEmail = rules.Any(static line => IsValidationRule(line, "EmailAddress"));
        var hasUrl = rules.Any(static line => IsValidationRule(line, "Url"));
        var regularExpressionRules = GetRegularExpressionRules(rules);
        if (!hasEmail && !hasUrl && regularExpressionRules.Count == 0)
        {
            return;
        }

        if (hasEmail)
        {
            sb.AppendLine("    private static readonly global::System.ComponentModel.DataAnnotations.EmailAddressAttribute s_emailAddressAttribute = new();");
        }

        if (hasUrl)
        {
            sb.AppendLine("    private static readonly global::System.ComponentModel.DataAnnotations.UrlAttribute s_urlAttribute = new();");
        }

        foreach (var rule in regularExpressionRules)
        {
            var pattern = DecodeValidationMessage(rule.Parts, 2, "");
            sb.AppendLine($"    private static readonly global::System.ComponentModel.DataAnnotations.RegularExpressionAttribute {rule.FieldName} = new({CSharpLiteral.String(pattern)}) {{ MatchTimeoutInMilliseconds = {rule.MatchTimeoutInMilliseconds} }};");
        }

        sb.AppendLine();
    }

    private static List<RegularExpressionRule> GetRegularExpressionRules(IEnumerable<string> rules)
    {
        var result = new List<RegularExpressionRule>();
        var emittedFieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in rules)
        {
            var parts = line.Split('|');
            if (parts.Length <= 3 || parts[1] != "RegularExpression")
            {
                continue;
            }

            var matchTimeoutInMilliseconds = GetRegularExpressionMatchTimeout(parts);
            var fieldName = RegularExpressionAttributeFieldName(parts[2], matchTimeoutInMilliseconds);
            if (emittedFieldNames.Add(fieldName))
            {
                result.Add(new RegularExpressionRule(parts, matchTimeoutInMilliseconds, fieldName));
            }
        }

        return result;
    }

    private static string GetRegularExpressionMatchTimeout(string[] parts)
        => parts.Length > 4 ? parts[4] : "2000";

    private static string RegularExpressionAttributeFieldName(string encodedPattern, string matchTimeoutInMilliseconds)
    {
        var value = encodedPattern + "_" + matchTimeoutInMilliseconds;
        var sb = new StringBuilder("s_regularExpressionAttribute_");
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('_');
                sb.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static bool IsValidationRule(string line, string ruleName)
    {
        var parts = line.Split('|');
        return parts.Length > 1 && parts[1] == ruleName;
    }

    private static void EmitAddValidationError(StringBuilder sb, string propertyName, string message)
    {
        sb.AppendLine("        {");
        sb.AppendLine($"            __AddValidationError(ref errors, {CSharpLiteral.String(propertyName)}, {CSharpLiteral.String(message)});");
        sb.AppendLine("        }");
    }

    private static string DecodeValidationMessage(string[] parts, int index, string fallback)
    {
        if (parts.Length <= index)
        {
            return fallback;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(parts[index]));
    }

    private static string SanitizeIdentifier(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        return sb.ToString();
    }

    private sealed record RegularExpressionRule(
        string[] Parts,
        string MatchTimeoutInMilliseconds,
        string FieldName);
}
