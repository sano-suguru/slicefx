using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.Lambda.APIGatewayEvents;

namespace SliceFx.Lambda.FunctionPerFeature.Tests;

public sealed class LambdaJsonBodyReaderTests
{
    [Fact]
    public async Task ReadAsync_returns_default_when_body_is_missing()
    {
        var ctx = LambdaTestHelpers.CreateContext(new APIGatewayHttpApiV2ProxyRequest());

        var value = await LambdaJsonBodyReader.ReadAsync(ctx, LambdaTestJsonContext.Default.Person);

        Assert.Null(value);
    }

    [Fact]
    public async Task ReadAsync_deserializes_utf8_json_body()
    {
        var ctx = LambdaTestHelpers.CreateContext(new APIGatewayHttpApiV2ProxyRequest
        {
            Body = /*lang=json,strict*/ """{"name":"Alice","age":30}""",
        });

        var value = await LambdaJsonBodyReader.ReadAsync(ctx, LambdaTestJsonContext.Default.Person);

        Assert.Equal(new Person("Alice", 30), value);
    }

    [Fact]
    public async Task ReadAsync_deserializes_base64_json_body()
    {
        var json = /*lang=json,strict*/ """{"name":"Alice","age":30}""";
        var ctx = LambdaTestHelpers.CreateContext(new APIGatewayHttpApiV2ProxyRequest
        {
            Body = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
            IsBase64Encoded = true,
        });

        var value = await LambdaJsonBodyReader.ReadAsync(ctx, LambdaTestJsonContext.Default.Person);

        Assert.Equal(new Person("Alice", 30), value);
    }

    [Fact]
    public async Task ReadAsync_throws_json_exception_for_malformed_json()
    {
        var ctx = LambdaTestHelpers.CreateContext(new APIGatewayHttpApiV2ProxyRequest
        {
            Body = """{"name":""",
        });

        await Assert.ThrowsAsync<JsonException>(async () =>
            await LambdaJsonBodyReader.ReadAsync(ctx, LambdaTestJsonContext.Default.Person));
    }

    [Fact]
    public async Task ReadAsync_throws_format_exception_for_invalid_base64()
    {
        var ctx = LambdaTestHelpers.CreateContext(new APIGatewayHttpApiV2ProxyRequest
        {
            Body = "not base64",
            IsBase64Encoded = true,
        });

        await Assert.ThrowsAsync<FormatException>(async () =>
            await LambdaJsonBodyReader.ReadAsync(ctx, LambdaTestJsonContext.Default.Person));
    }
}

internal sealed record Person(string Name, int Age);

[JsonSerializable(typeof(Person))]
[JsonSerializable(typeof(string))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class LambdaTestJsonContext : JsonSerializerContext;
