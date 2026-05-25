using Microsoft.CodeAnalysis;

namespace SliceFx.SourceGenerator;

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
    /// Diagnostic reported when a feature return type cannot be used by the WASI route table.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedReturnTypeForWasi = new(
        "SLICE008",
        "Return type not supported in WASI path",
        "Feature '{0}': return type '{1}' is ASP.NET-specific and will be excluded from the WASI route table. Use 'WasiResponse' or a POCO return type.",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when generated WASI JSON metadata cannot be produced safely.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingWasiJsonContext = new(
        "SLICE009",
        "WASI JSON source-generation metadata cannot be generated",
        "Feature '{0}' needs WASI JSON metadata but Slice cannot generate it safely: {1}",
        Category,
        DiagnosticSeverity.Warning,
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

    /// <summary>
    /// Diagnostic reported when a WASI route would need reflection-based DataAnnotations validation.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedValidationForWasi = new(
        "SLICE011",
        "DataAnnotations validation is not supported in WASI path",
        "Feature '{0}' uses DataAnnotations validation that requires reflection and will be excluded from the WASI route table. Use supported validation attributes or ISliceValidator<T>.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when a route, query, header, or body parameter type is unsupported by WASI binding.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedParameterForWasi = new(
        "SLICE023",
        "Parameter type not supported in WASI path",
        "Feature '{0}': parameter '{1}' of type '{2}' cannot be bound by the WASI route table and the feature will be excluded",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when referenced Slice feature assemblies exist but aggregation is not explicitly configured.
    /// </summary>
    public static readonly DiagnosticDescriptor UnconfiguredReferencedSliceModules = new(
        "SLICE024",
        "Referenced Slice modules require explicit aggregation",
        "Referenced Slice feature assemblies were found but cross-assembly aggregation is not configured: {0}. Set SliceFxReferencedAssemblies to an explicit allow-list, set SliceFxAggregateReferences=true to aggregate all referenced Slice modules, or set SliceFxAggregateReferences=false to keep local-only routes.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when SliceFxAggregateReferences is set to an unsupported value.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidSliceFxAggregateReferences = new(
        "SLICE025",
        "Invalid SliceFxAggregateReferences value",
        "SliceFxAggregateReferences value '{0}' is invalid. Use true/false, 1/0, or yes/no.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when a feature return type cannot be used by Lambda function-per-feature handlers.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedReturnTypeForLambdaFunctionPerFeature = new(
        "SLICE012",
        "Return type not supported in Lambda function-per-feature path",
        "Feature '{0}': return type '{1}' is not supported in Lambda function-per-feature handlers and the feature will be excluded. Use a POCO, Task<T>, ValueTask<T>, or APIGatewayHttpApiV2ProxyResponse return type.",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when a feature uses endpoint filters unsupported by Lambda function-per-feature handlers.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedFilterForLambdaFunctionPerFeature = new(
        "SLICE013",
        "Endpoint filter not supported in Lambda function-per-feature path",
        "Feature '{0}' uses endpoint filters and will be excluded from Lambda function-per-feature handlers",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when Lambda function-per-feature JSON metadata cannot be produced safely.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingLambdaJsonContext = new(
        "SLICE014",
        "Lambda JSON source-generation metadata cannot be generated",
        "Feature '{0}' needs Lambda JSON metadata but Slice cannot generate it safely: {1}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when a route or query parameter type is unsupported by Lambda function-per-feature binding.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedParameterForLambdaFunctionPerFeature = new(
        "SLICE015",
        "Parameter type not supported in Lambda function-per-feature path",
        "Feature '{0}': parameter '{1}' of type '{2}' cannot be bound by Lambda function-per-feature handlers and the feature will be excluded",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when a Lambda function-per-feature route would need reflection-based DataAnnotations validation.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedValidationForLambdaFunctionPerFeature = new(
        "SLICE016",
        "DataAnnotations validation is not supported in Lambda function-per-feature path",
        "Feature '{0}' uses DataAnnotations validation that requires reflection and will be excluded from Lambda function-per-feature handlers. Use supported validation attributes or ISliceValidator<T>.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when a Lambda function-per-feature startup type cannot be constructed by generated handlers.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidLambdaFunctionPerFeatureStartupType = new(
        "SLICE017",
        "Lambda function-per-feature startup type is invalid",
        "Lambda function-per-feature startup type '{0}' must implement ILambdaFunctionPerFeatureStartup and define a public parameterless constructor",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when multiple explicit JSON context overrides target the same Slice adapter.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateJsonContextOverride = new(
        "SLICE018",
        "Duplicate Slice JSON context override",
        "Slice JSON target '{0}' has multiple explicit context overrides: '{1}' and '{2}'. Use exactly one context per target.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when an explicit JSON context override is not a JsonSerializerContext.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidJsonContextOverride = new(
        "SLICE019",
        "Invalid Slice JSON context override",
        "Slice JSON context override '{0}' must derive from System.Text.Json.Serialization.JsonSerializerContext",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when an ISliceValidator&lt;T&gt; implementation cannot be generated safely.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidSliceValidator = new(
        "SLICE020",
        "Invalid Slice validator",
        "Slice validator '{0}' is invalid: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when one assembly contains multiple validators for the same request type.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateSliceValidator = new(
        "SLICE021",
        "Duplicate Slice validator",
        "Request type '{0}' has multiple ISliceValidator<T> implementations across generated Slice modules: '{1}' and '{2}'. Use a single validator or combine the rules in one validator.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when an ISliceValidator&lt;T&gt; implementation does not match a discovered Slice request parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor UnmatchedSliceValidator = new(
        "SLICE022",
        "Slice validator does not match a Slice request",
        "Slice validator '{0}' targets '{1}', but no discovered Slice feature uses that type as a request parameter",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
