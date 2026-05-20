using Microsoft.Extensions.DependencyInjection;
using Slice.Workers.Dispatch;
using Slice.Workers.Routing;

namespace Slice.Workers;

/// <summary>
/// Configures services and routes for a Slice Workers application.
/// </summary>
public sealed class WorkerHostBuilder
{
    internal WorkerHostBuilder() { }

    /// <summary>
    /// Gets the service collection used to register dependencies for Worker route handlers.
    /// </summary>
    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <summary>
    /// Adds routes to the Worker route table.
    /// </summary>
    /// <param name="configure">A callback that adds routes to the route table.</param>
    /// <returns>The current builder so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <c>null</c>.</exception>
    public WorkerHostBuilder AddRoutes(Action<WorkerRouteTable> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Routes);
        return this;
    }

    internal WorkerRouteTable Routes { get; } = new WorkerRouteTable();

    /// <summary>
    /// Builds the Worker application and prepares route invokers from the configured services.
    /// </summary>
    /// <returns>A Worker application ready to dispatch requests.</returns>
    public WorkerApp Build()
    {
        var services = Services.BuildServiceProvider();
        var dispatcher = new WorkerDispatcher(Routes, services);
        dispatcher.BuildInvokers();
        return new WorkerApp(services, dispatcher);
    }
}
