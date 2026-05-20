using Microsoft.CodeAnalysis;

namespace Slice.SourceGenerator;

internal static class SliceDiagnostics
{
    private const string Category = "Slice";

    public static readonly DiagnosticDescriptor MissingHandleMethod = new(
        "SLICE001",
        "Missing Handle method",
        "Feature '{0}' must define a 'public static Handle' method",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HandleNotPublicStatic = new(
        "SLICE002",
        "Handle method must be public and static",
        "Feature '{0}': the Handle method must be 'public static'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidRouteFormat = new(
        "SLICE003",
        "Invalid route format",
        "Feature '{0}': route '{1}' must be in 'METHOD /path' form",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateEndpointName = new(
        "SLICE004",
        "Duplicate endpoint name",
        "Endpoint name '{0}' is used by both '{1}' and '{2}'. Use distinct feature class names or set FeatureAttribute.Tag to disambiguate.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TagInferenceFallback = new(
        "SLICE006",
        "Tag inference fell back to Default",
        "Feature '{0}': no '.Features.' segment found in namespace — tag defaults to 'Default'",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedReturnTypeForWorkers = new(
        "SLICE008",
        "Return type not supported in Workers path",
        "Feature '{0}': return type '{1}' is ASP.NET-specific and will be excluded from the Workers route table. Use 'WorkerResponse' or a POCO return type.",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}
