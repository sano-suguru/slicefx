using Slice.Wasi.Dispatch;

namespace Slice.Wasi;

/// <summary>
/// A built Slice WASI application that can dispatch requests in-process.
/// </summary>
public sealed class WasiApp : IAsyncDisposable
{
    private readonly WasiDispatcher _dispatcher;

    internal WasiApp(IServiceProvider services, WasiDispatcher dispatcher)
    {
        Services = services;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Gets the root service provider for the WASI application.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Dispatches a single request through the generated WASI route table.
    /// </summary>
    /// <param name="request">The request to match and invoke.</param>
    /// <param name="ct">A token that is canceled when dispatch should stop.</param>
    /// <returns>The response returned by the matched route, or a 404 problem response when no route matches.</returns>
    public Task<WasiResponse> DispatchAsync(WasiRequest request, CancellationToken ct = default)
        => _dispatcher.DispatchAsync(request, ct);

    /// <summary>
    /// Disposes the underlying service provider when it implements <see cref="IAsyncDisposable"/> or <see cref="IDisposable"/>.
    /// </summary>
    /// <returns>A value task that completes when disposal is finished.</returns>
    public ValueTask DisposeAsync()
    {
        if (Services is IAsyncDisposable ad)
        {
            return ad.DisposeAsync();
        }

        if (Services is IDisposable d)
        {
            d.Dispose();
        }

        return default;
    }
}
