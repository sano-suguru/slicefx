namespace SliceFx.Lambda.FunctionPerFeature;

/// <summary>
/// Configures services for one generated Lambda function-per-feature handler.
/// </summary>
/// <param name="startupType">
/// Startup type implementing <see cref="ILambdaFunctionPerFeatureStartup"/> for the annotated feature.
/// </param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class LambdaFunctionStartupAttribute(Type startupType) : Attribute
{
    /// <summary>
    /// Gets the feature-scoped startup type.
    /// </summary>
    public Type StartupType { get; } = startupType;
}
