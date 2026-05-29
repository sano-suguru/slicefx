using System.Net.Http.Json;

namespace SliceFx.Wasi.IntegrationTests;

/// <summary>
/// Round-trip tests against a running Spin process.
/// Set <c>SPIN_ENDPOINT=http://localhost:3000</c> and run <c>spin up</c> before executing.
/// All tests are skipped when the environment variable is not set.
/// </summary>
public sealed class SpinRoundTripTests
{
    private readonly string? _endpoint = Environment.GetEnvironmentVariable("SPIN_ENDPOINT");

    [Fact(Skip = "Requires Spin: set SPIN_ENDPOINT env var and run 'spin up'")]
    public async Task PostItem_ReturnsCreated()
    {
        if (_endpoint is null)
        {
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri(_endpoint) };
        var response = await http.PostAsJsonAsync(
            "/api/items",
            new { url = "https://example.com" },
            TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
    }

    [Fact(Skip = "Requires Spin: set SPIN_ENDPOINT env var and run 'spin up'")]
    public async Task GetItems_ReturnsOk()
    {
        if (_endpoint is null)
        {
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri(_endpoint) };
        var response = await http.GetAsync("/api/items", TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
