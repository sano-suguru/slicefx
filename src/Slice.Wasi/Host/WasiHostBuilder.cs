using Microsoft.Extensions.DependencyInjection;
using Slice.Wasi.Dispatch;
using Slice.Wasi.Routing;

namespace Slice.Wasi;

/// <summary>
/// Configures services and routes for a Slice WASI application.
/// </summary>
public sealed class WasiHostBuilder
{
    internal WasiHostBuilder() { }

    /// <summary>
    /// Gets the service collection used to register dependencies for WASI route handlers.
    /// </summary>
    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <summary>
    /// Adds routes to the WASI route table.
    /// </summary>
    /// <param name="configure">A callback that adds routes to the route table.</param>
    /// <returns>The current builder so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <c>null</c>.</exception>
    public WasiHostBuilder AddRoutes(Action<WasiRouteTable> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Routes);
        return this;
    }

    internal WasiRouteTable Routes { get; } = new WasiRouteTable();

    /// <summary>
    /// Builds the WASI application and prepares route invokers from the configured services.
    /// </summary>
    /// <returns>A WASI application ready to dispatch requests.</returns>
    public WasiApp Build()
    {
        var services = Services.BuildServiceProvider();
        var dispatcher = new WasiDispatcher(Routes, services);
        dispatcher.BuildInvokers();
        return new WasiApp(services, dispatcher);
    }
}
