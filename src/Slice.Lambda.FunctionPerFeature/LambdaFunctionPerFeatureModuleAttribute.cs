namespace Slice.Lambda.FunctionPerFeature;

/// <summary>
/// Identifies an assembly containing generated Slice Lambda function-per-feature handlers.
/// </summary>
/// <param name="handlerTypeName">The generated handler type name.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class LambdaFunctionPerFeatureModuleAttribute(string handlerTypeName) : Attribute
{
    /// <summary>
    /// Gets the generated handler type name.
    /// </summary>
    public string HandlerTypeName { get; } = handlerTypeName;
}
