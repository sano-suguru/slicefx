using Microsoft.CodeAnalysis;

namespace Slice.SourceGenerator;

internal static class SliceDiagnostics
{
    private const string Category = "Slice";

    /// <summary>
    /// Diagnostic reported when a feature type does not define a Handle method.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingHandleMethod = new(
        "SLICE001",
        "Missing Handle method",
        "Feature '{0}' must define a 'public static Handle' method",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when a feature Handle method is not public and static.
    /// </summary>
    public static readonly DiagnosticDescriptor HandleNotPublicStatic = new(
        "SLICE002",
        "Handle method must be public and static",
        "Feature '{0}': the Handle method must be 'public static'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when a feature route is not in METHOD /path form.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidRouteFormat = new(
        "SLICE003",
        "Invalid route format",
        "Feature '{0}': route '{1}' must be in 'METHOD /path' form",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when two features generate the same endpoint name.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateEndpointName = new(
        "SLICE004",
        "Duplicate endpoint name",
        "Endpoint name '{0}' is used by both '{1}' and '{2}'. Use distinct feature class names or set FeatureAttribute.Tag to disambiguate.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when a feature type defines multiple Handle methods.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousHandleMethod = new(
        "SLICE005",
        "Ambiguous Handle method",
        "Feature '{0}' must define exactly one 'public static Handle' method",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when tag inference cannot find a .Features. namespace segment.
    /// </summary>
    public static readonly DiagnosticDescriptor TagInferenceFallback = new(
        "SLICE006",
        "Tag inference fell back to Default",
        "Feature '{0}': no '.Features.' segment found in namespace — tag defaults to 'Default'",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when a feature return type cannot be used by the Workers route table.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedReturnTypeForWorkers = new(
        "SLICE008",
        "Return type not supported in Workers path",
        "Feature '{0}': return type '{1}' is ASP.NET-specific and will be excluded from the Workers route table. Use 'WorkerResponse' or a POCO return type.",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when Workers body binding falls back to reflection-based JSON.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingWorkersJsonContext = new(
        "SLICE009",
        "Workers JSON source-generation context not found",
        "Feature '{0}' has a request body but no WorkerJsonContext was found, so Workers body binding will use reflection-based JSON deserialization",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when a feature's [Filter&lt;T&gt;] declarations place filters in an order
    /// that contradicts a [FilterOrderHint(After = typeof(...))] declared on one of the filters.
    /// </summary>
    public static readonly DiagnosticDescriptor FilterOrderViolation = new(
        "SLICE010",
        "Filter order violates declared hint",
        "Feature '{0}': filter '{1}' is annotated [FilterOrderHint(After = typeof({2}))] but is declared before it. Reorder the [Filter<T>] attributes so '{1}' follows '{2}'.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
