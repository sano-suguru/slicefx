namespace SliceFx;

/// <summary>
/// Marks an application-provided JSON source-generation context for a Slice target adapter.
/// </summary>
/// <remarks>
/// Use this attribute on a <see cref="System.Text.Json.Serialization.JsonSerializerContext" />
/// that contains the JSON roots required by the target adapter.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SliceJsonContextAttribute(SliceJsonTarget target) : Attribute
{
    /// <summary>
    /// Gets the Slice target that should use the marked JSON context.
    /// </summary>
    public SliceJsonTarget Target { get; } = target;
}

/// <summary>
/// Identifies target adapters that can use an explicit Slice JSON context override.
/// </summary>
public enum SliceJsonTarget
{
    /// <summary>
    /// Use the marked context for generated WASI route dispatch.
    /// </summary>
    Wasi = 1,

    /// <summary>
    /// Use the marked context for generated Lambda function-per-feature handlers.
    /// </summary>
    LambdaFunctionPerFeature = 2,
}
