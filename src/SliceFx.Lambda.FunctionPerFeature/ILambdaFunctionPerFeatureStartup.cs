using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Lambda.FunctionPerFeature;

/// <summary>
/// Configures services used by generated Lambda function-per-feature handlers.
/// </summary>
public interface ILambdaFunctionPerFeatureStartup
{
    /// <summary>
    /// Adds application services to the Lambda function-per-feature service collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    void ConfigureServices(IServiceCollection services);
}
