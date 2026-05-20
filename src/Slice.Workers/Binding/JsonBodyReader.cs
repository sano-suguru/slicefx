using System.Text.Json;
using Slice.Workers.Routing;

namespace Slice.Workers.Binding;

public static class JsonBodyReader
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ValueTask<T?> ReadAsync<T>(WorkerInvokerContext ctx)
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
}
