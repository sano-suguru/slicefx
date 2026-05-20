using System.Globalization;
using System.Text;

namespace Slice.Cli.Templates;

internal sealed record FilterSpec(string RootNamespace, string FilterName);

internal static class FilterTemplate
{
    internal static string Render(FilterSpec spec)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"namespace {spec.RootNamespace}.Filters;");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"public sealed class {spec.FilterName} : IEndpointFilter");
        sb.AppendLine("{");
        sb.AppendLine("    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)");
        sb.AppendLine("    {");
        sb.AppendLine("        // TODO: pre-processing");
        sb.AppendLine("        var result = await next(context);");
        sb.AppendLine("        // TODO: post-processing");
        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
