namespace Slice.Workers.Routing;

public sealed class WorkerInvokerContext
{
    public WorkerRequest Request { get; }
    public IServiceProvider Services { get; }
    public CancellationToken CancellationToken { get; }
    public IReadOnlyDictionary<string, string> RouteValues { get; }

    internal WorkerInvokerContext(
        WorkerRequest request,
        IServiceProvider services,
        IReadOnlyDictionary<string, string> routeValues,
        CancellationToken cancellationToken)
    {
        Request = request;
        Services = services;
        RouteValues = routeValues;
        CancellationToken = cancellationToken;
    }
}
