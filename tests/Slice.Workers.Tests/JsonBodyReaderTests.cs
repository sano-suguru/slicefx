using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Slice.Workers.Binding;
using Slice.Workers.Routing;

namespace Slice.Workers.Tests;

public class JsonBodyReaderTests
{
    [Fact]
    public async Task ReadAsync_returns_default_for_empty_body()
    {
        var ctx = CreateContext([]);

        var value = await JsonBodyReader.ReadAsync<Request>(ctx);

        Assert.Null(value);
    }

    [Fact]
    public async Task ReadAsync_throws_json_exception_for_malformed_json()
    {
        var ctx = CreateContext(Encoding.UTF8.GetBytes("{"));

        await Assert.ThrowsAsync<JsonException>(async () => await JsonBodyReader.ReadAsync<Request>(ctx));
    }

    private static WorkerInvokerContext CreateContext(byte[]? body)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var request = new WorkerRequest("POST", "/test", new Dictionary<string, string>(), null, body);
        return new WorkerInvokerContext(request, services, new Dictionary<string, string>(), CancellationToken.None);
    }

    private sealed record Request(string Name);
}
