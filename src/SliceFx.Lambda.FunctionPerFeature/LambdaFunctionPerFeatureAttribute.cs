namespace SliceFx.Lambda.FunctionPerFeature;

/// <summary>
/// Opts an assembly into generated Slice Lambda function-per-feature handlers.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class LambdaFunctionPerFeatureAttribute : Attribute
{
}
