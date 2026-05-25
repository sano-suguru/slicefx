namespace SliceFx.Wasi;

/// <summary>
/// Represents an HTTP-like request dispatched through the Slice WASI runtime.
/// </summary>
/// <param name="Method">The HTTP method to match, such as <c>GET</c> or <c>POST</c>.</param>
/// <param name="Path">The request path to match against registered WASI routes.</param>
/// <param name="Headers">The request headers supplied by the wasi:http host.</param>
/// <param name="QueryString">The raw query string, with or without a leading <c>?</c>; <c>null</c> when absent.</param>
/// <param name="Body">The raw request body bytes; <c>null</c> when the request has no body.</param>
public sealed record WasiRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    string? QueryString,
    byte[]? Body);
