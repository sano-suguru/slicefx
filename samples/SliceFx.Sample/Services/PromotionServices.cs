using System.Collections.Concurrent;

namespace SliceFx.Sample.Services;

/// <summary>
/// Clock abstraction used to demonstrate keyed DI in the promote-user feature.
/// </summary>
public interface IClock
{
    /// <summary>Returns the current UTC time.</summary>
    DateTimeOffset GetUtcNow();
}

/// <summary>
/// System-clock implementation of <see cref="IClock"/>.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
}

/// <summary>
/// In-memory audit log used to demonstrate concrete-class DI in the promote-user feature.
/// </summary>
public sealed class AuditLog
{
    private readonly ConcurrentQueue<string> _entries = new();

    /// <summary>Records an audit entry.</summary>
    public Task RecordAsync(string entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.Enqueue($"[{DateTimeOffset.UtcNow:o}] {entry}");
        return Task.CompletedTask;
    }

    /// <summary>Returns all recorded entries in insertion order.</summary>
    public IReadOnlyList<string> GetEntries() => [.. _entries];
}
