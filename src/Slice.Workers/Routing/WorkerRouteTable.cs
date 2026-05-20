namespace Slice.Workers.Routing;

public sealed class WorkerRouteTable
{
    private readonly List<WorkerRoute> _routes = [];

    public void Add(
        string method,
        string pattern,
        Func<IServiceProvider, Func<WorkerInvokerContext, Task<WorkerResponse>>> invokerFactory)
    {
        _routes.Add(new WorkerRoute(
            method.ToUpperInvariant(),
            new WorkerRoutePattern(pattern),
            invokerFactory));
    }

    internal IReadOnlyList<WorkerRoute> Routes => _routes;
}

internal sealed class WorkerRoute(
    string method,
    WorkerRoutePattern pattern,
    Func<IServiceProvider, Func<WorkerInvokerContext, Task<WorkerResponse>>> invokerFactory)
{
    public string Method { get; } = method;
    public WorkerRoutePattern Pattern { get; } = pattern;
    public Func<IServiceProvider, Func<WorkerInvokerContext, Task<WorkerResponse>>> InvokerFactory { get; } = invokerFactory;
    public Func<WorkerInvokerContext, Task<WorkerResponse>>? Invoker { get; set; }
}
