namespace SliceFx.Wasi.KeyValue;

/// <summary>
/// Abstraction over a key-value store for use in SliceFx WASI feature handlers.
/// </summary>
/// <remarks>
/// On Fermyon Cloud / Spin, implement this interface using the WIT-generated
/// <c>wasi:keyvalue/store@0.2.0-draft</c> bucket bindings produced by componentize-dotnet,
/// and register the implementation in <c>WasiHostBuilder.Services</c>. Use
/// <see cref="InMemoryKeyValueStore"/> in unit tests.
/// </remarks>
public interface IKeyValueStore
{
    /// <summary>Gets the raw bytes stored at <paramref name="key"/>, or <c>null</c> if the key does not exist.</summary>
    ValueTask<byte[]?> GetBytesAsync(string key, CancellationToken ct = default);

    /// <summary>Stores raw bytes at <paramref name="key"/>, overwriting any existing value.</summary>
    ValueTask SetBytesAsync(string key, ReadOnlyMemory<byte> value, CancellationToken ct = default);

    /// <summary>Removes <paramref name="key"/> from the store. No-op if the key does not exist.</summary>
    ValueTask DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Returns <c>true</c> if <paramref name="key"/> exists in the store.</summary>
    ValueTask<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>Returns all keys currently in the store.</summary>
    ValueTask<IReadOnlyList<string>> ListKeysAsync(CancellationToken ct = default);
}
