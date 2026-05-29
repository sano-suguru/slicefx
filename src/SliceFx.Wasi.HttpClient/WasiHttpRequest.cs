namespace SliceFx.Wasi.HttpClient;

/// <summary>
/// Represents an outgoing HTTP request sent through the WASI HTTP client.
/// </summary>
/// <param name="Method">The HTTP method, such as <c>GET</c> or <c>POST</c>.</param>
/// <param name="Url">The absolute URL of the target resource.</param>
/// <param name="Headers">Optional request headers; <c>null</c> sends no extra headers.</param>
/// <param name="Body">Optional request body bytes; <c>null</c> sends no body.</param>
public sealed record WasiHttpRequest(
    string Method,
    string Url,
    IReadOnlyDictionary<string, string>? Headers,
    byte[]? Body);
