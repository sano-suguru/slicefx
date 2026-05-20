using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Slice.Workers.Routing;

namespace Slice.Workers.Binding;

public static class JsonBodyReader
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

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

        try
        {
            var result = JsonSerializer.Deserialize<T>(body, s_options);
            return new ValueTask<T?>(result);
        }
        catch (JsonException)
        {
            return new ValueTask<T?>(default(T));
        }
    }

    public static ValueTask<T?> ReadAsync<T>(WorkerInvokerContext ctx, JsonTypeInfo<T> jsonTypeInfo)
    {
        var body = ctx.Request.Body;
        if (body is null || body.Length == 0)
        {
            return new ValueTask<T?>(default(T));
        }

        try
        {
            var result = JsonSerializer.Deserialize(body, jsonTypeInfo);
            return new ValueTask<T?>(result);
        }
        catch (JsonException)
        {
            return new ValueTask<T?>(default(T));
        }
    }
}
