namespace Slice.Wasi;

/// <summary>
/// Represents an HTTP-like response returned from a Slice WASI route.
/// </summary>
/// <param name="Status">The HTTP status code to return to the wasi:http host.</param>
/// <param name="Headers">The response headers to return to the wasi:http host.</param>
/// <param name="Body">The raw response body bytes; use an empty array for responses without a body.</param>
public sealed record WasiResponse(
    int Status,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body);
