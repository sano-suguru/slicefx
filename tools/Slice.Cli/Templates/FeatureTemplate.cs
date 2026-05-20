using System.Globalization;
using System.Text;

namespace Slice.Cli.Templates;

internal sealed record FeatureSpec(
    string RootNamespace,
    string Group,
    string FeatureName,
    string HttpMethod,
    string Route);

internal static class FeatureTemplate
{
    private static readonly HashSet<string> BodyMethods = ["POST", "PUT", "PATCH"];

    internal static string Render(FeatureSpec spec)
    {
        var method = spec.HttpMethod.ToUpperInvariant();
        var hasRequest = BodyMethods.Contains(method);
        var featureText = EscapeStringLiteral($"{method} {spec.Route}");
        var needsSliceUsing = !spec.RootNamespace.Equals("Slice", StringComparison.Ordinal)
                              && !spec.RootNamespace.StartsWith("Slice.", StringComparison.Ordinal);

        var sb = new StringBuilder();

        if (needsSliceUsing)
        {
            sb.AppendLine("using Slice;");
            sb.AppendLine();
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"namespace {spec.RootNamespace}.Features.{spec.Group};");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"[Feature(\"{featureText}\", Summary = \"{featureText}\")]");
        sb.AppendLine(CultureInfo.InvariantCulture, $"public static class {spec.FeatureName}");
        sb.AppendLine("{");

        if (hasRequest)
        {
            sb.AppendLine("    public record Request();");
            sb.AppendLine();
        }

        sb.AppendLine("    public record Response();");
        sb.AppendLine();

        if (hasRequest)
        {
            sb.AppendLine("    public static Task<IResult> Handle(Request req, CancellationToken ct)");
        }
        else
        {
            sb.AppendLine("    public static Task<IResult> Handle(CancellationToken ct)");
        }

        sb.AppendLine("    {");
        sb.AppendLine("        // TODO: implement handler");
        sb.AppendLine("        return Task.FromResult<IResult>(Results.Ok(new Response()));");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string EscapeStringLiteral(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            _ = c switch
            {
                '\\' => sb.Append(@"\\"),
                '"' => sb.Append("\\\""),
                '\0' => sb.Append(@"\0"),
                '\a' => sb.Append(@"\a"),
                '\b' => sb.Append(@"\b"),
                '\f' => sb.Append(@"\f"),
                '\n' => sb.Append(@"\n"),
                '\r' => sb.Append(@"\r"),
                '\t' => sb.Append(@"\t"),
                '\v' => sb.Append(@"\v"),
                _ when char.IsControl(c) => sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}"),
                _ => sb.Append(c),
            };
        }

        return sb.ToString();
    }
}
