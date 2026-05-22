namespace Slice.Wasi.Routing;

/// <summary>
/// Provides request, service, cancellation, and route-value state to a WASI route invoker.
/// </summary>
public sealed class WasiInvokerContext
{
    /// <summary>
    /// Gets the request currently being dispatched.
    /// </summary>
    public WasiRequest Request { get; }

    /// <summary>
    /// Gets the scoped service provider for the current request.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the cancellation token associated with the current dispatch operation.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets route parameter values captured from the matched route pattern.
    /// </summary>
    public IReadOnlyDictionary<string, string> RouteValues { get; }

    internal WasiInvokerContext(
        WasiRequest request,
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
