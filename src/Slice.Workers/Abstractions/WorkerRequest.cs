namespace Slice.Workers;

public sealed record WorkerRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    string? QueryString,
    byte[]? Body);
