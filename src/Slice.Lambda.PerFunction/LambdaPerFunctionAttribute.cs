namespace Slice.Lambda.PerFunction;

/// <summary>
/// Opts an assembly into generated Slice Lambda per-feature handlers.
/// </summary>
/// <param name="startupType">
/// Optional startup type implementing <see cref="ILambdaPerFunctionStartup"/> for dependency injection configuration.
/// </param>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class LambdaPerFunctionAttribute(Type? startupType = null) : Attribute
{
    /// <summary>
    /// Gets the optional startup type used to configure services for generated handlers.
    /// </summary>
    public Type? StartupType { get; } = startupType;
}
