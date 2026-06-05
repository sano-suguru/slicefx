using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using SliceFx.Wasi.Binding;
using SliceFx.Wasi.Routing;

namespace SliceFx.Wasi.Tests;

public class JsonBodyReaderTests
{
    [Fact]
    public async Task ReadAsync_returns_default_for_empty_body()
    {
        var ctx = CreateContext([]);

        var value = await JsonBodyReader.ReadAsync(ctx, WasiTestJsonContext.Default.JsonBodyReaderRequest);

        Assert.Null(value);
    }

    [Fact]
    public async Task ReadAsync_throws_json_exception_for_malformed_json()
    {
        var ctx = CreateContext(Encoding.UTF8.GetBytes("{"));

        await Assert.ThrowsAsync<JsonException>(async () =>
            await JsonBodyReader.ReadAsync(ctx, WasiTestJsonContext.Default.JsonBodyReaderRequest));
    }

    [Fact]
    public async Task ReadAsync_deserializes_utf8_json_body()
    {
        var ctx = CreateContext(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"name":"Alice"}"""));

        var value = await JsonBodyReader.ReadAsync(ctx, WasiTestJsonContext.Default.JsonBodyReaderRequest);

        Assert.Equal(new JsonBodyReaderRequest("Alice"), value);
    }

    private static WasiInvokerContext CreateContext(byte[]? body)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var request = new WasiRequest("POST", "/test", new Dictionary<string, string>(), null, body);
        return new WasiInvokerContext(request, services, new Dictionary<string, string>(), CancellationToken.None);
    }
}

internal sealed record JsonBodyReaderRequest(string Name);

[JsonSerializable(typeof(JsonBodyReaderRequest))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class WasiTestJsonContext : JsonSerializerContext;
