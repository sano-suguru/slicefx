namespace SliceFx.Core.Tests;

public class SliceFilterResultTests
{
    [Fact]
    public void ShortCircuit_sets_IsShortCircuit_true_and_stores_result()
    {
        var inner = SliceResult.Unauthorized("No key.");
        var r = SliceFilterResult.ShortCircuit(inner);

        Assert.True(r.IsShortCircuit);
        Assert.NotNull(r.ShortCircuitResult);
        Assert.Equal(401, r.ShortCircuitResult!.Value.Status);
        Assert.Equal("No key.", r.ShortCircuitResult!.Value.ProblemDetail);
        Assert.Null(r.HostResponse);
        Assert.Equal(401, r.Status);
    }

    [Fact]
    public void PassThrough_sets_IsShortCircuit_false_and_stores_response()
    {
        var hostResp = new object();
        var r = SliceFilterResult.PassThrough(hostResp, 200);

        Assert.False(r.IsShortCircuit);
        Assert.Null(r.ShortCircuitResult);
        Assert.Same(hostResp, r.HostResponse);
        Assert.Equal(200, r.Status);
    }

    [Fact]
    public void PassThrough_accepts_null_status()
    {
        var r = SliceFilterResult.PassThrough(new object(), null);

        Assert.False(r.IsShortCircuit);
        Assert.Null(r.Status);
    }

    [Fact]
    public void PassThrough_accepts_null_host_response()
    {
        var r = SliceFilterResult.PassThrough(null, 204);

        Assert.False(r.IsShortCircuit);
        Assert.Null(r.HostResponse);
        Assert.Equal(204, r.Status);
    }

    [Fact]
    public void ShortCircuit_captures_status_from_SliceResult_factory()
    {
        Assert.Equal(404, SliceFilterResult.ShortCircuit(SliceResult.NotFound()).Status);
        Assert.Equal(400, SliceFilterResult.ShortCircuit(SliceResult.BadRequest()).Status);
        Assert.Equal(429, SliceFilterResult.ShortCircuit(SliceResult.Problem(429, "Too Many Requests")).Status);
    }
}

public class SliceFilterContextTests
{
    [Fact]
    public void Constructor_stores_all_properties()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Foo"] = "bar" };
        var routeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "42" };
        var services = new TestServiceProvider();
        using var cts = new CancellationTokenSource();

        var ctx = new SliceFilterContext("GET", "/items/42", headers, routeValues, services, "1.2.3.4", cts.Token);

        Assert.Equal("GET", ctx.Method);
        Assert.Equal("/items/42", ctx.Path);
        Assert.Equal("bar", ctx.Headers["X-Foo"]);
        Assert.Equal("42", ctx.RouteValues["id"]);
        Assert.Same(services, ctx.Services);
        Assert.Equal("1.2.3.4", ctx.ClientIp);
        Assert.Equal(cts.Token, ctx.CancellationToken);
        // ResponseHeaders starts empty.
        Assert.Empty(ctx.ResponseHeaders);
    }

    [Fact]
    public void ResponseHeaders_starts_empty_writable_and_is_case_insensitive()
    {
        var ctx = new SliceFilterContext(
            "GET", "/items",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new TestServiceProvider(),
            clientIp: null,
            CancellationToken.None);

        // Initially empty
        Assert.Empty(ctx.ResponseHeaders);

        // Writable
        ctx.ResponseHeaders["Retry-After"] = "5";
        Assert.Equal("5", ctx.ResponseHeaders["Retry-After"]);

        // Case-insensitive key lookup
        Assert.Equal("5", ctx.ResponseHeaders["retry-after"]);
        Assert.Equal("5", ctx.ResponseHeaders["RETRY-AFTER"]);

        // Multiple distinct keys
        ctx.ResponseHeaders["X-RateLimit-Limit"] = "100";
        Assert.Equal(2, ctx.ResponseHeaders.Count);
        Assert.Equal("100", ctx.ResponseHeaders["x-ratelimit-limit"]);
    }

    [Fact]
    public void ResponseHeaders_overwrites_existing_key()
    {
        var ctx = new SliceFilterContext(
            "POST", "/items",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new TestServiceProvider(),
            clientIp: null,
            CancellationToken.None);

        ctx.ResponseHeaders["X-Custom"] = "first";
        ctx.ResponseHeaders["x-custom"] = "second";

        // Last write wins (same case-insensitive key)
        Assert.Single(ctx.ResponseHeaders);
        Assert.Equal("second", ctx.ResponseHeaders["X-Custom"]);
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

public class SliceAotFilterContextBuilderTests
{
    [Fact]
    public void Create_maps_RemoteIpAddress_to_ClientIp()
    {
        var httpCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.1");
        httpCtx.Request.Method = "GET";
        httpCtx.Request.Path = "/items";

        var filterCtx = SliceAotFilterContextBuilder.Create(httpCtx);

        Assert.Equal("192.168.1.1", filterCtx.ClientIp);
    }

    [Fact]
    public void Create_sets_ClientIp_null_when_RemoteIpAddress_not_set()
    {
        var httpCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        // RemoteIpAddress is null by default on DefaultHttpContext
        httpCtx.Request.Method = "GET";
        httpCtx.Request.Path = "/items";

        var filterCtx = SliceAotFilterContextBuilder.Create(httpCtx);

        Assert.Null(filterCtx.ClientIp);
    }

    [Fact]
    public void Create_returns_cached_context_on_second_call()
    {
        var httpCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");
        httpCtx.Request.Method = "GET";
        httpCtx.Request.Path = "/items";

        var first = SliceAotFilterContextBuilder.Create(httpCtx);
        var second = SliceAotFilterContextBuilder.Create(httpCtx);

        Assert.Same(first, second);
    }
}
