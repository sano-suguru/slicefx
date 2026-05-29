namespace SliceFx.Wasi.HttpClient;

/// <summary>
/// Abstraction over an outgoing HTTP client for use in SliceFx WASI feature handlers.
/// </summary>
/// <remarks>
/// On Fermyon Cloud / Spin, implement this interface using the WIT-generated
/// <c>wasi:http/outgoing-handler@0.2.0</c> bindings produced by componentize-dotnet,
/// using <c>FutureIncomingResponse.Subscribe().Block()</c> to synchronously wait for
/// the response. Use <see cref="InMemoryWasiHttpClient"/> in unit tests.
/// </remarks>
public interface IWasiHttpClient
{
    /// <summary>Sends an HTTP request and returns the response.</summary>
    /// <exception cref="WasiHttpException">The WASI host reported an error.</exception>
    ValueTask<WasiResponse> SendAsync(WasiHttpRequest request, CancellationToken ct = default);
}
