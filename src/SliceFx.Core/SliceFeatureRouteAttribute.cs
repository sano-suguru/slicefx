namespace SliceFx;

/// <summary>
/// Identifies one source-generated Slice route for cross-assembly diagnostics.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SliceFeatureRouteAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SliceFeatureRouteAttribute"/> class with route metadata.
    /// </summary>
    public SliceFeatureRouteAttribute(
        string endpointName,
        string featureType,
        string httpMethod,
        string pattern,
        string? tag,
        string? summary,
        string? requestType,
        string? returnType,
        string? portability,
        string? portabilityReason,
        string? serializedFilterTypes,
        string? serializedParameters,
        string? lambdaFunctionPerFeatureStatus,
        string? lambdaFunctionPerFeatureReason,
        string? lambdaFunctionPerFeatureHandlerAssembly,
        string? lambdaFunctionPerFeatureHandlerType,
        string? lambdaFunctionPerFeatureHandlerMethod,
        string manifestSchemaVersion,
        string? wasiDispatchStatus,
        string? wasiDispatchReason)
    {
        EndpointName = endpointName;
        FeatureType = featureType;
        HttpMethod = httpMethod;
        Pattern = pattern;
        Tag = tag;
        Summary = summary;
        RequestType = requestType;
        ReturnType = returnType;
        Portability = portability;
        PortabilityReason = portabilityReason;
        SerializedFilterTypes = serializedFilterTypes;
        SerializedParameters = serializedParameters;
        LambdaFunctionPerFeatureStatus = lambdaFunctionPerFeatureStatus;
        LambdaFunctionPerFeatureReason = lambdaFunctionPerFeatureReason;
        LambdaFunctionPerFeatureHandlerAssembly = lambdaFunctionPerFeatureHandlerAssembly;
        LambdaFunctionPerFeatureHandlerType = lambdaFunctionPerFeatureHandlerType;
        LambdaFunctionPerFeatureHandlerMethod = lambdaFunctionPerFeatureHandlerMethod;
        ManifestSchemaVersion = manifestSchemaVersion;
        WasiDispatchStatus = wasiDispatchStatus;
        WasiDispatchReason = wasiDispatchReason;
    }

    /// <summary>
    /// Gets the endpoint name generated for the feature.
    /// </summary>
    public string EndpointName { get; }

    /// <summary>
    /// Gets the feature type name.
    /// </summary>
    public string FeatureType { get; }

    /// <summary>
    /// Gets the route HTTP method.
    /// </summary>
    public string HttpMethod { get; }

    /// <summary>
    /// Gets the route pattern.
    /// </summary>
    public string Pattern { get; }

    /// <summary>
    /// Gets the generated route tag.
    /// </summary>
    public string? Tag { get; }

    /// <summary>
    /// Gets the generated route summary.
    /// </summary>
    public string? Summary { get; }

    /// <summary>
    /// Gets the request type name when the route has a Slice request body.
    /// </summary>
    public string? RequestType { get; }

    /// <summary>
    /// Gets the handler return type name.
    /// </summary>
    public string? ReturnType { get; }

    /// <summary>
    /// Gets the route portability classification.
    /// </summary>
    public string? Portability { get; }

    /// <summary>
    /// Gets the reason for the portability classification when one exists.
    /// </summary>
    public string? PortabilityReason { get; }

    /// <summary>
    /// Gets newline-separated filter type names.
    /// </summary>
    public string? SerializedFilterTypes { get; }

    /// <summary>
    /// Gets newline-separated handler parameters serialized as <c>Type|Name|Nullability|BindingSource|BindingName</c>.
    /// </summary>
    public string? SerializedParameters { get; }

    /// <summary>
    /// Gets the Lambda function-per-feature eligibility status.
    /// </summary>
    public string? LambdaFunctionPerFeatureStatus { get; }

    /// <summary>
    /// Gets the reason for the Lambda function-per-feature eligibility status when one exists.
    /// </summary>
    public string? LambdaFunctionPerFeatureReason { get; }

    /// <summary>
    /// Gets the assembly that contains the generated Lambda function-per-feature handler when emitted.
    /// </summary>
    public string? LambdaFunctionPerFeatureHandlerAssembly { get; }

    /// <summary>
    /// Gets the generated Lambda function-per-feature handler type when emitted.
    /// </summary>
    public string? LambdaFunctionPerFeatureHandlerType { get; }

    /// <summary>
    /// Gets the generated Lambda function-per-feature handler method when emitted.
    /// </summary>
    public string? LambdaFunctionPerFeatureHandlerMethod { get; }

    /// <summary>
    /// Gets the generated route manifest schema version.
    /// </summary>
    public string ManifestSchemaVersion { get; }

    /// <summary>
    /// Gets whether WASI dispatch was emitted for the route.
    /// </summary>
    public string? WasiDispatchStatus { get; }

    /// <summary>
    /// Gets why WASI dispatch was not emitted when excluded.
    /// </summary>
    public string? WasiDispatchReason { get; }
}
