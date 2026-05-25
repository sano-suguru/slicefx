namespace SliceFx;

/// <summary>
/// Identifies one source-generated Slice validator for cross-assembly diagnostics.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SliceFeatureValidatorAttribute(string requestType, string validatorType) : Attribute
{
    /// <summary>
    /// Gets the fully qualified request type name validated by the validator.
    /// </summary>
    public string RequestType { get; } = requestType;

    /// <summary>
    /// Gets the fully qualified validator implementation type name.
    /// </summary>
    public string ValidatorType { get; } = validatorType;
}
