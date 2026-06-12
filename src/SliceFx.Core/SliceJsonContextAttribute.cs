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

    /// <summary>
    /// Use the marked context for generated ASP.NET NativeAOT-safe route dispatch.
    /// </summary>
    /// <remarks>
    /// Required when <c>[assembly: SliceAspNetAot]</c> is set. The context must declare
    /// <c>[JsonSerializable]</c> entries for every request and response type used by the
    /// assembly's features. The generator reports <c>SLICE071</c> when a required root
    /// is missing.
    /// </remarks>
    AspNet = 3,
}
