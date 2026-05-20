using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Slice.Workers.Routing;

namespace Slice.Workers.Binding;

/// <summary>
/// Reads JSON request bodies for generated Slice Workers route invokers.
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
    /// <param name="ctx">The current Worker invoker context.</param>
    /// <returns>
    /// The deserialized value, or <c>default</c> when the body is empty or absent.
    /// </returns>
    /// <exception cref="JsonException">The request body contains malformed JSON.</exception>
    /// <remarks>This overload uses reflection-based JSON deserialization. Use <see cref="ReadAsync{T}(WorkerInvokerContext, JsonTypeInfo{T})"/> for NativeAOT and trimming.</remarks>
    public static ValueTask<T?> ReadAsync<T>(WorkerInvokerContext ctx)
        => ReadDynamicAsync<T>(ctx);

    [RequiresDynamicCode("Use ReadAsync<T>(WorkerInvokerContext, JsonTypeInfo<T>) for NativeAOT-compatible Workers request binding.")]
    [RequiresUnreferencedCode("Use ReadAsync<T>(WorkerInvokerContext, JsonTypeInfo<T>) for trim-compatible Workers request binding.")]
    private static ValueTask<T?> ReadDynamicAsync<T>(WorkerInvokerContext ctx)
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
    /// <param name="ctx">The current Worker invoker context.</param>
    /// <param name="jsonTypeInfo">The JSON metadata to use when deserializing the request body.</param>
    /// <returns>
    /// The deserialized value, or <c>default</c> when the body is empty or absent.
    /// </returns>
    /// <exception cref="JsonException">The request body contains malformed JSON.</exception>
    public static ValueTask<T?> ReadAsync<T>(WorkerInvokerContext ctx, JsonTypeInfo<T> jsonTypeInfo)
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
