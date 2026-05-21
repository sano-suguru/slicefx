namespace Slice;

/// <summary>
/// Identifies one source-generated Slice route for cross-assembly diagnostics.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SliceFeatureRouteAttribute(
    string endpointName,
    string featureType,
    string httpMethod,
    string pattern) : Attribute
{
    /// <summary>
    /// Gets the endpoint name generated for the feature.
    /// </summary>
    public string EndpointName { get; } = endpointName;

    /// <summary>
    /// Gets the feature type name.
    /// </summary>
    public string FeatureType { get; } = featureType;

    /// <summary>
    /// Gets the route HTTP method.
    /// </summary>
    public string HttpMethod { get; } = httpMethod;

    /// <summary>
    /// Gets the route pattern.
    /// </summary>
    public string Pattern { get; } = pattern;
}
