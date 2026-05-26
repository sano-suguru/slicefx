using SliceFx.Lambda.FunctionPerFeature;
using SliceFx.LambdaFunctionPerFeatureSample.Services;

namespace SliceFx.LambdaFunctionPerFeatureSample.Features.Orders;

/// <summary>
/// Per-feature DI setup for all order features.
/// Each function-per-feature artifact calls <see cref="ConfigureServices"/> once at cold-start
/// and owns an isolated DI container — singletons here are never shared with other features.
/// </summary>
public sealed class OrderFeatureStartup : ILambdaFunctionPerFeatureStartup
{
    /// <summary>
    /// Registers the in-memory order store as a singleton for this feature's process.
    /// </summary>
    /// <param name="services">The feature-scoped service collection.</param>
    public void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IOrderStore, InMemoryOrderStore>();
}
