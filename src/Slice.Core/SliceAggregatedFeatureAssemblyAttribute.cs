namespace Slice;

/// <summary>
/// Identifies a referenced Slice feature assembly aggregated by a source-generated host module.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SliceAggregatedFeatureAssemblyAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SliceAggregatedFeatureAssemblyAttribute"/> class.
    /// </summary>
    /// <param name="assemblyName">The simple name of the aggregated feature assembly.</param>
    public SliceAggregatedFeatureAssemblyAttribute(string assemblyName) => AssemblyName = assemblyName;

    /// <summary>
    /// Gets the simple name of the aggregated feature assembly.
    /// </summary>
    public string AssemblyName { get; }
}
