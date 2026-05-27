extern alias SliceApp;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SliceFx.Testing;

namespace SliceFx.TestHost.Tests;

public sealed class SliceRuntimeHttpTests
{
    [Fact]
    public async Task Generated_slice_endpoint_handles_valid_http_request_through_test_host()
    {
        await using var host = SliceTestHost.Create<SliceApp::Program>();

        using var response = await host.Client.PostAsJsonAsync("/widgets", new { name = "Alice" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("executed", Assert.Single(response.Headers.GetValues("X-Slice-Filter")));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        Assert.Equal("Alice", root.GetProperty("name").GetString());
        Assert.Equal("store", root.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Generated_slice_endpoint_returns_validation_problem_for_data_annotations_failure()
    {
        await using var host = SliceTestHost.Create<SliceApp::Program>();

        using var response = await host.Client.PostAsJsonAsync("/widgets", new { name = "" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Slice-Filter"));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = document.RootElement;
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.True(root.GetProperty("errors").TryGetProperty("Name", out var errors));
        Assert.NotEmpty(errors.EnumerateArray());
    }

    [Fact]
    public async Task Generated_slice_endpoint_returns_validation_problem_for_slice_validator_failure()
    {
        await using var host = SliceTestHost.Create<SliceApp::Program>();

        using var response = await host.Client.PostAsJsonAsync("/widgets", new { name = "blocked" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Slice-Filter"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var errors = document.RootElement.GetProperty("errors").GetProperty("Name");
        Assert.Equal("Name is blocked.", errors[0].GetString());
    }

    [Fact]
    public async Task Concrete_registered_DI_service_without_FromServices_resolves_from_DI_not_as_body_on_aspnet_path()
    {
        // AuditRecorder is a concrete class registered via AddSingleton<AuditRecorder>().
        // On the ASP.NET path the generated code is plain Minimal API: IServiceProviderIsService
        // returns true for AuditRecorder, so ASP.NET resolves it from DI without [FromServices].
        // The Request record binds from the JSON body. This pin verifies that SliceFx's ASP.NET
        // path behaves identically to raw Minimal API and does NOT apply the portable-dispatch
        // concrete/interface heuristic used by the WASI/Lambda generators.
        await using var host = SliceTestHost.Create<SliceApp::Program>();

        using var response = await host.Client.PostAsJsonAsync("/audit", new { message = "hello" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("hello", document.RootElement.GetProperty("recorded").GetString());
    }

    [Fact]
    public async Task Generated_slice_endpoint_short_circuits_data_annotations_before_slice_validator()
    {
        await using var host = SliceTestHost.Create<SliceApp::Program>();

        using var response = await host.Client.PostAsJsonAsync("/widgets", new { name = "x" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Slice-Filter"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var errors = document.RootElement.GetProperty("errors").GetProperty("Name");
        var messages = errors.EnumerateArray().Select(static error => error.GetString()).ToArray();
        Assert.NotEmpty(messages);
        Assert.DoesNotContain("Name failed Slice validator.", messages);
    }
}
