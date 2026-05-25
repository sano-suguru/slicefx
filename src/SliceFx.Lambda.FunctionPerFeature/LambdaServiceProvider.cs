using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Lambda.FunctionPerFeature;

/// <summary>
/// Builds service providers for generated Lambda function-per-feature handlers.
/// </summary>
public static class LambdaServiceProvider
{
    /// <summary>
    /// Builds a root service provider for generated handlers.
    /// </summary>
    /// <param name="configure">Optional application service configuration.</param>
    /// <returns>The root service provider created at cold start.</returns>
    public static ServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }
}
