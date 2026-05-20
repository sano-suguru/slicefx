namespace Slice.Workers.Routing;

/// <summary>
/// Stores Worker routes registered by generated Slice Workers code or custom route configuration.
/// </summary>
public sealed class WorkerRouteTable
{
    private readonly List<WorkerRoute> _routes = [];

    /// <summary>
    /// Adds a route to the Worker route table.
    /// </summary>
    /// <param name="method">The HTTP method to match. It is normalized to uppercase invariant form.</param>
    /// <param name="pattern">The route pattern to match, such as <c>/users/{id:guid}</c>.</param>
    /// <param name="invokerFactory">
    /// A factory invoked during application build. It receives the root service provider and returns
    /// a per-request invoker that receives a <see cref="WorkerInvokerContext"/>.
    /// </param>
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
