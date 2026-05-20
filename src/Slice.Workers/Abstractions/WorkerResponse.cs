namespace Slice.Workers;

public sealed record WorkerResponse(
    int Status,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body);
