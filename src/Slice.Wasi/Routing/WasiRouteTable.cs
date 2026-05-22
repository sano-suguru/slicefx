namespace Slice.Wasi.Routing;

/// <summary>
/// Stores WASI routes registered by generated Slice WASI code or custom route configuration.
/// </summary>
public sealed class WasiRouteTable
{
    private readonly List<WasiRoute> _routes = [];

    /// <summary>
    /// Adds a route to the WASI route table.
    /// </summary>
    /// <param name="method">The HTTP method to match. It is normalized to uppercase invariant form.</param>
    /// <param name="pattern">The route pattern to match, such as <c>/users/{id:guid}</c>.</param>
    /// <param name="invokerFactory">
    /// A factory invoked during application build. It receives the root service provider and returns
    /// a per-request invoker that receives a <see cref="WasiInvokerContext"/>.
    /// </param>
    public void Add(
        string method,
        string pattern,
        Func<IServiceProvider, Func<WasiInvokerContext, Task<WasiResponse>>> invokerFactory)
    {
        _routes.Add(new WasiRoute(
            method.ToUpperInvariant(),
            new WasiRoutePattern(pattern),
            invokerFactory));
    }

    internal IReadOnlyList<WasiRoute> Routes => _routes;
}

internal sealed class WasiRoute(
    string method,
    WasiRoutePattern pattern,
    Func<IServiceProvider, Func<WasiInvokerContext, Task<WasiResponse>>> invokerFactory)
{
    public string Method { get; } = method;
    public WasiRoutePattern Pattern { get; } = pattern;
    public Func<IServiceProvider, Func<WasiInvokerContext, Task<WasiResponse>>> InvokerFactory { get; } = invokerFactory;
    public Func<WasiInvokerContext, Task<WasiResponse>>? Invoker { get; set; }
}
