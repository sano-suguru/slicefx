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

        using var response = await host.Client.PostAsJsonAsync("/widgets", new { name = "Alice" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("executed", Assert.Single(response.Headers.GetValues("X-Slice-Filter")));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        Assert.Equal("Alice", root.GetProperty("name").GetString());
        Assert.Equal("store", root.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Generated_slice_endpoint_returns_validation_problem_for_data_annotations_failure()
    {
        await using var host = SliceTestHost.Create<SliceApp::Program>();

        using var response = await host.Client.PostAsJsonAsync("/widgets", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Slice-Filter"));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.True(root.GetProperty("errors").TryGetProperty("Name", out var errors));
        Assert.NotEmpty(errors.EnumerateArray());
    }

    [Fact]
    public async Task Generated_slice_endpoint_returns_validation_problem_for_slice_validator_failure()
    {
        await using var host = SliceTestHost.Create<SliceApp::Program>();

        using var response = await host.Client.PostAsJsonAsync("/widgets", new { name = "blocked" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Slice-Filter"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors").GetProperty("Name");
        Assert.Equal("Name is blocked.", errors[0].GetString());
    }

    [Fact]
    public async Task Generated_slice_endpoint_short_circuits_data_annotations_before_slice_validator()
    {
        await using var host = SliceTestHost.Create<SliceApp::Program>();

        using var response = await host.Client.PostAsJsonAsync("/widgets", new { name = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Slice-Filter"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors").GetProperty("Name");
        var messages = errors.EnumerateArray().Select(static error => error.GetString()).ToArray();
        Assert.NotEmpty(messages);
        Assert.DoesNotContain("Name failed Slice validator.", messages);
    }
}
