using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SliceFx.Wasi.KeyValue;

/// <summary>
/// Convenience methods for reading and writing UTF-8 strings and JSON values via <see cref="IKeyValueStore"/>.
/// </summary>
public static class KeyValueStoreExtensions
{
    /// <summary>Gets the value at <paramref name="key"/> decoded as a UTF-8 string, or <c>null</c> if not found.</summary>
    public static async ValueTask<string?> GetStringAsync(
        this IKeyValueStore store, string key, CancellationToken ct = default)
    {
        var bytes = await store.GetBytesAsync(key, ct).ConfigureAwait(false);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Stores <paramref name="value"/> as a UTF-8-encoded string at <paramref name="key"/>.</summary>
    public static ValueTask SetStringAsync(
        this IKeyValueStore store, string key, string value, CancellationToken ct = default) =>
        store.SetBytesAsync(key, Encoding.UTF8.GetBytes(value), ct);

    /// <summary>
    /// Deserializes a JSON value at <paramref name="key"/> using the provided <paramref name="typeInfo"/>.
    /// Returns <c>default</c> if the key does not exist.
    /// </summary>
    public static async ValueTask<T?> GetJsonAsync<T>(
        this IKeyValueStore store, string key, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        var bytes = await store.GetBytesAsync(key, ct).ConfigureAwait(false);
        if (bytes is null)
        {
            return default;
        }

        return JsonSerializer.Deserialize(bytes, typeInfo);
    }

    /// <summary>
    /// Serializes <paramref name="value"/> to JSON and stores it at <paramref name="key"/>.
    /// </summary>
    public static ValueTask SetJsonAsync<T>(
        this IKeyValueStore store, string key, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct = default) =>
        store.SetBytesAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, typeInfo), ct);
}
