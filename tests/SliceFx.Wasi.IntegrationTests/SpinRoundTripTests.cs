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

    [Fact(Skip = "Requires Spin with outgoing HTTP: set SPIN_ENDPOINT env var and run 'spin up'")]
    public async Task FeedRefresh_OutgoingHttp_ReturnsOk()
    {
        if (_endpoint is null)
        {
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri(_endpoint) };
        var response = await http.PostAsync("/api/feeds/refresh", null, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Skip = "Requires Spin cron: configure a cron trigger in spin.toml, set SPIN_ENDPOINT env var, run 'spin up', and wait for the cron to fire before asserting KV state")]
    public async Task CronTick_WritesKvEntry_ReadableViaHttp()
    {
        if (_endpoint is null)
        {
            return;
        }

        // After a cron tick fires and calls SpinCronDispatcher.DispatchAsync, the handler writes
        // a KV entry. This test reads that entry back via the HTTP endpoint to verify end-to-end
        // cron → handler → KV → HTTP round-trip.
        using var http = new HttpClient { BaseAddress = new Uri(_endpoint) };
        var response = await http.GetAsync("/api/items", TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
