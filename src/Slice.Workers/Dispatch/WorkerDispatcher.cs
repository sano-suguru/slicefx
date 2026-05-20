using Microsoft.Extensions.DependencyInjection;
using Slice.Workers.Routing;

namespace Slice.Workers.Dispatch;

internal sealed class WorkerDispatcher
{
    private readonly WorkerRouteTable _table;
    private readonly IServiceProvider _rootServices;

    internal WorkerDispatcher(WorkerRouteTable table, IServiceProvider rootServices)
    {
        _table = table;
        _rootServices = rootServices;
    }

    internal void BuildInvokers()
    {
        foreach (var route in _table.Routes)
        {
            route.Invoker = route.InvokerFactory(_rootServices);
        }
    }

    public async Task<WorkerResponse> DispatchAsync(WorkerRequest request, CancellationToken ct = default)
    {
        var method = request.Method.ToUpperInvariant();
        foreach (var route in _table.Routes)
        {
            if (!string.Equals(route.Method, method, StringComparison.Ordinal))
            {
                continue;
            }

            if (!route.Pattern.TryMatch(request.Path, out var routeValues))
            {
                continue;
            }

            using var scope = _rootServices.CreateScope();
            var ctx = new WorkerInvokerContext(request, scope.ServiceProvider, routeValues, ct);
            return await route.Invoker!(ctx).ConfigureAwait(false);
        }

        return SliceResult.Problem(404, "Not Found", $"No route matched {method} {request.Path}");
    }
}
