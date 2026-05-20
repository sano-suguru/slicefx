using Slice.Workers.Dispatch;
using Slice.Workers.Ipc;

namespace Slice.Workers;

/// <summary>
/// A built Slice Workers application that can dispatch requests in-process or run the JSON-lines IPC loop.
/// </summary>
public sealed class WorkerApp : IAsyncDisposable
{
    private readonly WorkerDispatcher _dispatcher;

    internal WorkerApp(IServiceProvider services, WorkerDispatcher dispatcher)
    {
        Services = services;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Gets the root service provider for the Worker application.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Dispatches a single request through the generated Worker route table.
    /// </summary>
    /// <param name="request">The request to match and invoke.</param>
    /// <param name="ct">A token that is canceled when dispatch should stop.</param>
    /// <returns>The response returned by the matched route, or a 404 problem response when no route matches.</returns>
    public Task<WorkerResponse> DispatchAsync(WorkerRequest request, CancellationToken ct = default)
        => _dispatcher.DispatchAsync(request, ct);

    /// <summary>
    /// Runs the worker in stdin/stdout JSON IPC mode without an async entry point.
    /// This is intended for WASI command hosts that do not support blocking on async Main.
    /// </summary>
    /// <param name="ct">A token that stops the read loop when canceled.</param>
    public void Run(CancellationToken ct = default)
    {
        using var reader = new StreamReader(Console.OpenStandardInput());
        using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        string? line;
        while (!ct.IsCancellationRequested && (line = reader.ReadLine()) is not null)
        {
            WorkerResponse response;
            try
            {
                var request = JsonProtocol.ParseRequest(line);
                response = request is null
                    ? SliceResult.Problem(400, "Bad Request", "Could not parse request JSON.")
                    : _dispatcher.DispatchAsync(request, ct).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                response = SliceResult.Problem(500, "Internal Server Error", "An unexpected error occurred.");
            }

            writer.WriteLine(JsonProtocol.SerializeResponse(response));
        }
    }

    /// <summary>
    /// Runs the worker in stdin/stdout JSON IPC mode (P2: WASI command invocation).
    /// Each line of stdin is one serialized <see cref="WorkerRequest"/> JSON.
    /// Each line of stdout is one serialized <see cref="WorkerResponse"/> JSON.
    /// </summary>
    /// <param name="ct">A token that stops the read loop when canceled.</param>
    /// <returns>A task that completes when stdin closes or cancellation stops the loop.</returns>
    public async Task RunAsync(CancellationToken ct = default)
    {
        using var reader = new StreamReader(Console.OpenStandardInput());
        using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        string? line;
        while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            WorkerResponse response;
            try
            {
                var request = JsonProtocol.ParseRequest(line);
                response = request is null
                    ? SliceResult.Problem(400, "Bad Request", "Could not parse request JSON.")
                    : await _dispatcher.DispatchAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
                response = SliceResult.Problem(500, "Internal Server Error", "An unexpected error occurred.");
            }

            await writer.WriteLineAsync(JsonProtocol.SerializeResponse(response)).ConfigureAwait(false);
        }
    }

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
