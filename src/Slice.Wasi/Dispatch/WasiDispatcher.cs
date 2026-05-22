using Microsoft.Extensions.DependencyInjection;
using Slice.Wasi.Routing;

namespace Slice.Wasi.Dispatch;

internal sealed class WasiDispatcher
{
    private readonly WasiRouteTable _table;
    private readonly IServiceProvider _rootServices;

    internal WasiDispatcher(WasiRouteTable table, IServiceProvider rootServices)
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

    public async Task<WasiResponse> DispatchAsync(WasiRequest request, CancellationToken ct = default)
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

            await using var scope = _rootServices.CreateAsyncScope();
            var ctx = new WasiInvokerContext(request, scope.ServiceProvider, routeValues, ct);
            return await route.Invoker!(ctx).ConfigureAwait(false);
        }

        return SliceResult.Problem(404, "Not Found", $"No route matched {method} {request.Path}");
    }
}
