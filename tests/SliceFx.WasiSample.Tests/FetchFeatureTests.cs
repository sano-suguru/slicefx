using System.Text;
using System.Text.Json;
using SliceFx.Wasi.HttpClient;

namespace SliceFx.WasiSample.Tests;

public sealed class FetchFeatureTests
{
    private static readonly IReadOnlyDictionary<string, string> JsonHeaders =
        new Dictionary<string, string> { ["Content-Type"] = "application/json" };

    private static WasiRequest PostFetch(string json) =>
        new("POST", "/fetch", JsonHeaders, QueryString: null, Body: Encoding.UTF8.GetBytes(json));

    [Fact]
    public async Task Fetch_demo_url_extracts_title()
    {
        await using var app = SampleWasiApp.Create();

        var response = await app.DispatchAsync(
            PostFetch($$"""{"url":"{{SampleWasiApp.DemoFetchUrl}}"}"""),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.Status);
        var result = JsonSerializer.Deserialize(response.Body, WasiJsonContext.Default.PostFetchResponse)!;
        Assert.Equal("Hello from SliceFx", result.Title);
    }

    [Fact]
    public async Task Fetch_invalid_url_returns_400()
    {
        await using var app = SampleWasiApp.Create();

        var response = await app.DispatchAsync(
            PostFetch(/*lang=json,strict*/ """{"url":"not-a-url"}"""),
            TestContext.Current.CancellationToken);

        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task Fetch_upstream_error_returns_502()
    {
        // Inject a client that returns a non-2xx for any URL to exercise the error path.
        var failing = new InMemoryWasiHttpClient().Respond(
            _ => true,
            new WasiResponse(500, new Dictionary<string, string>(), []));
        await using var app = SampleWasiApp.Create(httpClient: failing);

        var response = await app.DispatchAsync(
            PostFetch(/*lang=json,strict*/ """{"url":"https://example.com/"}"""),
            TestContext.Current.CancellationToken);

        Assert.Equal(502, response.Status);
    }
}
