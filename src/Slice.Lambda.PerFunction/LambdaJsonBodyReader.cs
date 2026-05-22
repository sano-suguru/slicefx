using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Slice.Lambda.PerFunction;

/// <summary>
/// Reads JSON request bodies from API Gateway HTTP API v2 events.
/// </summary>
public static class LambdaJsonBodyReader
{
    /// <summary>
    /// Deserializes the current request body as JSON using source-generated metadata.
    /// </summary>
    public static ValueTask<T?> ReadAsync<T>(LambdaInvocationContext ctx, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        if (string.IsNullOrEmpty(ctx.Request.Body))
        {
            return new ValueTask<T?>(default(T));
        }

        var bytes = ctx.Request.IsBase64Encoded
            ? Convert.FromBase64String(ctx.Request.Body)
            : Encoding.UTF8.GetBytes(ctx.Request.Body);

        var value = JsonSerializer.Deserialize(bytes, jsonTypeInfo);
        return new ValueTask<T?>(value);
    }
}
