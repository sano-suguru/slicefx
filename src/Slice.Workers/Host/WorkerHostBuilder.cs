using Microsoft.Extensions.DependencyInjection;
using Slice.Workers.Dispatch;
using Slice.Workers.Routing;

namespace Slice.Workers;

public sealed class WorkerHostBuilder
{
    internal WorkerHostBuilder() { }

    public IServiceCollection Services { get; } = new ServiceCollection();

    public WorkerHostBuilder AddRoutes(Action<WorkerRouteTable> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Routes);
        return this;
    }

    internal WorkerRouteTable Routes { get; } = new WorkerRouteTable();

    public WorkerApp Build()
    {
        var services = Services.BuildServiceProvider();
        var dispatcher = new WorkerDispatcher(Routes, services);
        dispatcher.BuildInvokers();
        return new WorkerApp(services, dispatcher);
    }
}
