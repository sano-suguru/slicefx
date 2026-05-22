using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Slice.Wasi.Routing;

namespace Slice.Wasi.Binding;

/// <summary>
/// Reads JSON request bodies for generated Slice WASI route invokers.
/// </summary>
public static class JsonBodyReader
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Deserializes the current request body as JSON.
    /// </summary>
    /// <typeparam name="T">The target request body type.</typeparam>
    /// <param name="ctx">The current WASI invoker context.</param>
    /// <returns>
    /// The deserialized value, or <c>default</c> when the body is empty or absent.
    /// </returns>
    /// <exception cref="JsonException">The request body contains malformed JSON.</exception>
    /// <remarks>This overload uses reflection-based JSON deserialization. Use <see cref="ReadAsync{T}(WasiInvokerContext, JsonTypeInfo{T})"/> for NativeAOT and trimming.</remarks>
    [RequiresDynamicCode("Use ReadAsync<T>(WasiInvokerContext, JsonTypeInfo<T>) for NativeAOT-compatible WASI request binding.")]
    [RequiresUnreferencedCode("Use ReadAsync<T>(WasiInvokerContext, JsonTypeInfo<T>) for trim-compatible WASI request binding.")]
    public static ValueTask<T?> ReadAsync<T>(WasiInvokerContext ctx)
    {
        var body = ctx.Request.Body;
        if (body is null || body.Length == 0)
        {
            return new ValueTask<T?>(default(T));
        }

        var result = JsonSerializer.Deserialize<T>(body, s_options);
        return new ValueTask<T?>(result);
    }

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
        var body = ctx.Request.Body;
        if (body is null || body.Length == 0)
        {
            return new ValueTask<T?>(default(T));
        }

        var result = JsonSerializer.Deserialize(body, jsonTypeInfo);
        return new ValueTask<T?>(result);
    }
}
