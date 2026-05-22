using Microsoft.Extensions.DependencyInjection;

namespace Slice.Lambda.PerFunction;

/// <summary>
/// Configures services used by generated Lambda per-feature handlers.
/// </summary>
public interface ILambdaPerFunctionStartup
{
    /// <summary>
    /// Adds application services to the per-function Lambda service collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    void ConfigureServices(IServiceCollection services);
}
