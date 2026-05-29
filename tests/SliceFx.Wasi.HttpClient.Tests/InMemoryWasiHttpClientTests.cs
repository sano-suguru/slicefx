namespace SliceFx.Wasi.HttpClient.Tests;

public sealed class InMemoryWasiHttpClientTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>();

    [Fact]
    public async Task SendAsync_NoHandlers_Returns200EmptyBody()
    {
        var client = new InMemoryWasiHttpClient();
        var req = new WasiHttpRequest("GET", "https://example.com/", null, null);

        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);

        Assert.Equal(200, resp.Status);
        Assert.Empty(resp.Body);
    }

    [Fact]
    public async Task SendAsync_MatchingPredicate_ReturnsCannedResponse()
    {
        var expected = new WasiResponse(201, NoHeaders, [1, 2, 3]);
        var client = new InMemoryWasiHttpClient()
            .Respond(r => r.Url.Contains("example.com"), expected);

        var resp = await client.SendAsync(
            new WasiHttpRequest("POST", "https://example.com/items", null, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(201, resp.Status);
        Assert.Equal([1, 2, 3], resp.Body);
    }

    [Fact]
    public async Task SendAsync_NonMatchingPredicate_Returns200Fallback()
    {
        var client = new InMemoryWasiHttpClient()
            .Respond(r => r.Url.Contains("other.com"), new WasiResponse(404, NoHeaders, []));

        var resp = await client.SendAsync(
            new WasiHttpRequest("GET", "https://example.com/", null, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, resp.Status);
    }

    [Fact]
    public async Task SendAsync_FirstMatchWins()
    {
        var client = new InMemoryWasiHttpClient()
            .Respond(_ => true, new WasiResponse(200, NoHeaders, []))
            .Respond(_ => true, new WasiResponse(500, NoHeaders, []));

        var resp = await client.SendAsync(
            new WasiHttpRequest("GET", "https://example.com/", null, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, resp.Status);
    }

    [Fact]
    public async Task SendAsync_WithBody_PassesBodyToPredicate()
    {
        byte[]? captured = null;
        var client = new InMemoryWasiHttpClient()
            .Respond(r => { captured = r.Body; return true; }, new WasiResponse(200, NoHeaders, []));

        await client.SendAsync(
            new WasiHttpRequest("POST", "https://example.com/", null, [42]),
            TestContext.Current.CancellationToken);

        Assert.Equal([42], captured);
    }

    [Fact]
    public async Task SendAsync_WithHeaders_PassesHeadersToPredicate()
    {
        IReadOnlyDictionary<string, string>? captured = null;
        var client = new InMemoryWasiHttpClient()
            .Respond(r => { captured = r.Headers; return true; }, new WasiResponse(200, NoHeaders, []));
        var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };

        await client.SendAsync(
            new WasiHttpRequest("GET", "https://example.com/", headers, null),
            TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("application/json", captured["Accept"]);
    }

    [Fact]
    public async Task Clear_RemovesAllHandlers()
    {
        var client = new InMemoryWasiHttpClient()
            .Respond(_ => true, new WasiResponse(404, NoHeaders, []));
        client.Clear();

        var resp = await client.SendAsync(
            new WasiHttpRequest("GET", "https://example.com/", null, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, resp.Status);
    }

    [Fact]
    public void Respond_NullPredicate_Throws()
    {
        var client = new InMemoryWasiHttpClient();
        Assert.Throws<ArgumentNullException>(() => client.Respond(null!, new WasiResponse(200, NoHeaders, [])));
    }

    [Fact]
    public void Respond_NullResponse_Throws()
    {
        var client = new InMemoryWasiHttpClient();
        Assert.Throws<ArgumentNullException>(() => client.Respond(_ => true, null!));
    }

    [Fact]
    public async Task SendAsync_NullRequest_Throws()
    {
        var client = new InMemoryWasiHttpClient();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.SendAsync(null!, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task SendAsync_MatchByMethod()
    {
        var client = new InMemoryWasiHttpClient()
            .Respond(r => r.Method == "POST", new WasiResponse(201, NoHeaders, []))
            .Respond(r => r.Method == "GET", new WasiResponse(200, NoHeaders, []));

        var post = await client.SendAsync(
            new WasiHttpRequest("POST", "https://example.com/", null, null),
            TestContext.Current.CancellationToken);
        var get = await client.SendAsync(
            new WasiHttpRequest("GET", "https://example.com/", null, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(201, post.Status);
        Assert.Equal(200, get.Status);
    }
}
