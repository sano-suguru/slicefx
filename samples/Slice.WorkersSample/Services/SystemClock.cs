namespace Slice.WorkersSample.Services;

/// <summary>
/// Production clock implementation backed by <see cref="DateTimeOffset.UtcNow" />.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
