using System.Collections.Concurrent;

namespace SliceFx.Wasi.KeyValue;

/// <summary>
/// Thread-safe in-memory <see cref="IKeyValueStore"/> implementation for unit tests and local development.
/// </summary>
public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public ValueTask<byte[]?> GetBytesAsync(string key, CancellationToken ct = default)
    {
        _store.TryGetValue(key, out var value);
        return ValueTask.FromResult<byte[]?>(value);
    }

    /// <inheritdoc/>
    public ValueTask SetBytesAsync(string key, ReadOnlyMemory<byte> value, CancellationToken ct = default)
    {
        _store[key] = value.ToArray();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DeleteAsync(string key, CancellationToken ct = default)
    {
        _store.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        ValueTask.FromResult(_store.ContainsKey(key));

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<string>> ListKeysAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<string>>([.. _store.Keys]);

    /// <summary>Removes all entries. Useful for resetting state between tests.</summary>
    public void Clear() => _store.Clear();
}
