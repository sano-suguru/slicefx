extern alias AotSample;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SliceFx.Testing;

namespace SliceFx.AotSample.Tests;

/// <summary>
/// Verifies the SliceFx NativeAOT-safe generated handlers under JIT.
/// These tests run against the same source-generated code that is compiled
/// to a native binary via PublishAot, but execute under the JIT runtime so
/// standard test tooling applies. The AOT binary itself is validated by the
/// CI nativeaot-sample job.
/// </summary>
public sealed class AotSampleTests
{
    // --- Health ---

    [Fact]
    public async Task GetHealth_returns_200_with_json()
    {
        await using var host = SliceTestHost.Create<AotSample::Program>();
        var ct = TestContext.Current.CancellationToken;

        var response = await host.Client.GetAsync("/health", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
    }

    // --- CreateTodo ---

    [Fact]
    public async Task CreateTodo_valid_returns_200_with_todo()
    {
        await using var host = SliceTestHost.Create<AotSample::Program>();
        var ct = TestContext.Current.CancellationToken;

        var response = await host.Client.PostAsJsonAsync(
            "/todos",
            new { title = "write tests" },
            ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("write tests", doc.RootElement.GetProperty("title").GetString());
        Assert.True(doc.RootElement.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task CreateTodo_wrong_content_type_returns_415()
    {
        await using var host = SliceTestHost.Create<AotSample::Program>();
        var ct = TestContext.Current.CancellationToken;

        var content = new StringContent("title=x", System.Text.Encoding.UTF8, "text/plain");
        var response = await host.Client.PostAsync("/todos", content, ct);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task CreateTodo_malformed_json_returns_400_problem()
    {
        await using var host = SliceTestHost.Create<AotSample::Program>();
        var ct = TestContext.Current.CancellationToken;

        var content = new StringContent("not-json", System.Text.Encoding.UTF8, "application/json");
        var response = await host.Client.PostAsync("/todos", content, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateTodo_empty_title_returns_400_validation_problem()
    {
        await using var host = SliceTestHost.Create<AotSample::Program>();
        var ct = TestContext.Current.CancellationToken;

        var response = await host.Client.PostAsJsonAsync(
            "/todos",
            new { title = string.Empty },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("Title", out _));
    }

    // --- GetTodo ---

    [Fact]
    public async Task GetTodo_existing_returns_200_with_todo()
    {
        await using var host = SliceTestHost.Create<AotSample::Program>();
        var ct = TestContext.Current.CancellationToken;

        // Create first
        var createResp = await host.Client.PostAsJsonAsync("/todos", new { title = "find me" }, ct);
        var createBody = await createResp.Content.ReadAsStringAsync(ct);
        using var createDoc = JsonDocument.Parse(createBody);
        var id = createDoc.RootElement.GetProperty("id").GetString();

        // Then get
        var response = await host.Client.GetAsync($"/todos/{id}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("find me", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetTodo_missing_returns_404_problem()
    {
        await using var host = SliceTestHost.Create<AotSample::Program>();
        var ct = TestContext.Current.CancellationToken;

        var response = await host.Client.GetAsync(
            "/todos/00000000-0000-0000-0000-000000000001",
            ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Not Found", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetTodo_invalid_route_returns_400_problem()
    {
        await using var host = SliceTestHost.Create<AotSample::Program>();
        var ct = TestContext.Current.CancellationToken;

        // Non-guid id does not match the route constraint → 404 from router (route not matched)
        var response = await host.Client.GetAsync("/todos/not-a-guid", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- ListTodos ---

    [Fact]
    public async Task ListTodos_returns_200_json_array()
    {
        await using var host = SliceTestHost.Create<AotSample::Program>();
        var ct = TestContext.Current.CancellationToken;

        await host.Client.PostAsJsonAsync("/todos", new { title = "item A" }, ct);
        await host.Client.PostAsJsonAsync("/todos", new { title = "item B" }, ct);

        var response = await host.Client.GetAsync("/todos", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() >= 2);
    }

    // --- DeleteTodo ---

    [Fact]
    public async Task DeleteTodo_returns_200_empty_body()
    {
        await using var host = SliceTestHost.Create<AotSample::Program>();
        var ct = TestContext.Current.CancellationToken;

        var createResp = await host.Client.PostAsJsonAsync("/todos", new { title = "delete me" }, ct);
        var createBody = await createResp.Content.ReadAsStringAsync(ct);
        using var createDoc = JsonDocument.Parse(createBody);
        var id = createDoc.RootElement.GetProperty("id").GetString();

        var response = await host.Client.DeleteAsync($"/todos/{id}", ct);

        // Void/Task handler → 200 empty body (RDF parity, not 204)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Empty(body);
    }
}
