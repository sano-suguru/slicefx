using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SliceFx.Wasi.Routing;

namespace SliceFx.Wasi.Binding;

/// <summary>
/// Reads JSON request bodies for generated Slice WASI route invokers.
/// </summary>
public static class JsonBodyReader
{
    /// <summary>
    /// Deserializes the current request body as JSON using source-generated metadata.
    /// </summary>
    /// <typeparam name="T">The target request body type.</typeparam>
    /// <param name="ctx">The current WASI invoker context.</param>
    /// <param name="jsonTypeInfo">The JSON metadata to use when deserializing the request body.</param>
    /// <returns>
    /// The deserialized value, or <c>default</c> when the body is empty or absent.
    /// </returns>
    /// <exception cref="JsonException">The request body contains malformed JSON.</exception>
    public static ValueTask<T?> ReadAsync<T>(WasiInvokerContext ctx, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        var body = ctx.Request.Body;
        if (body is null || body.Length == 0)
        {
            return new ValueTask<T?>(default(T));
        }

        var result = JsonSerializer.Deserialize(body, jsonTypeInfo);
        return new ValueTask<T?>(result);
    }
}
