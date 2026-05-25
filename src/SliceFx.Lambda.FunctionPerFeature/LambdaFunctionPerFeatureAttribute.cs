namespace SliceFx.Lambda.FunctionPerFeature;

/// <summary>
/// Opts an assembly into generated Slice Lambda function-per-feature handlers.
/// </summary>
/// <param name="startupType">
/// Optional startup type implementing <see cref="ILambdaFunctionPerFeatureStartup"/> for dependency injection configuration.
/// </param>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class LambdaFunctionPerFeatureAttribute(Type? startupType = null) : Attribute
{
    /// <summary>
    /// Gets the optional startup type used to configure services for generated handlers.
    /// </summary>
    public Type? StartupType { get; } = startupType;
}
