namespace SliceFx;

/// <summary>
/// Identifies a source-generated Slice registration module for cross-assembly aggregation.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SliceFeatureModuleAttribute(Type registrationType) : Attribute
{
    /// <summary>
    /// Gets the generated registration type for one Slice feature module.
    /// </summary>
    public Type RegistrationType { get; } = registrationType;
}
