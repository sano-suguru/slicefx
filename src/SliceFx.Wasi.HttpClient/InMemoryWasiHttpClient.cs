namespace SliceFx.Wasi.HttpClient;

/// <summary>
/// In-memory <see cref="IWasiHttpClient"/> implementation for unit tests and local development.
/// Register response handlers with <see cref="Respond"/> before calling <see cref="SendAsync"/>.
/// </summary>
public sealed class InMemoryWasiHttpClient : IWasiHttpClient
{
    private readonly List<(Func<WasiHttpRequest, bool> Predicate, WasiResponse Response)> _handlers = [];

    /// <summary>
    /// Registers a canned response for requests that match <paramref name="predicate"/>.
    /// Handlers are evaluated in registration order; the first match wins.
    /// </summary>
    public InMemoryWasiHttpClient Respond(Func<WasiHttpRequest, bool> predicate, WasiResponse response)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(response);
        _handlers.Add((predicate, response));
        return this;
    }

    /// <summary>
    /// Returns the first registered response whose predicate matches <paramref name="request"/>,
    /// or a 200 OK with an empty body if no handler matches.
    /// </summary>
    public ValueTask<WasiResponse> SendAsync(WasiHttpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var (predicate, response) in _handlers)
        {
            if (predicate(request))
            {
                return ValueTask.FromResult(response);
            }
        }

        return ValueTask.FromResult(new WasiResponse(200, new Dictionary<string, string>(), []));
    }

    /// <summary>Removes all registered handlers. Useful for resetting state between tests.</summary>
    public void Clear() => _handlers.Clear();
}
