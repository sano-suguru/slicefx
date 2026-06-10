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

    [Fact]
    public async Task PromoteWidget_resolves_route_body_FromServices_interface_and_keyed_dependencies()
    {
        await using var host = SliceTestHost.Create<SliceApp::Program>();

        using var response = await host.Client.PostAsJsonAsync(
            "/widgets/7/promote",
            new { tier = "gold" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = document.RootElement;
        Assert.Equal(7, root.GetProperty("id").GetInt32());
        Assert.Equal("gold", root.GetProperty("tier").GetString());
        Assert.Equal("promote:7:gold", root.GetProperty("audit").GetString());
        Assert.Equal("promotion-clock", root.GetProperty("clock").GetString());
    }

    [Fact]
    public async Task PromoteWidget_validates_inferred_body_dto_not_concrete_service()
    {
        await using var host = SliceTestHost.Create<SliceApp::Program>();

        using var response = await host.Client.PostAsJsonAsync(
            "/widgets/7/promote",
            new { tier = "x" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("Tier", out var tierErrors));
        Assert.NotEmpty(tierErrors.EnumerateArray());
    }

    // ── SliceResult non-JSON result types (Redirect / Html) end-to-end ───────────

    [Fact]
    public async Task SliceResult_Redirect_feature_returns_302_with_location_header()
    {
        // RedirectWidget returns SliceResult.Redirect("/widgets/1").
        // Without the generated __SliceResultToIResult wrapper, Minimal API would serialize
        // the struct as JSON (200 + struct body). This test captures the raw 302 to confirm
        // the wrapper correctly dispatches the redirect.
        await using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<SliceApp::Program>();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync("/widgets/redirect", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/widgets/1", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SliceResult_Html_feature_returns_200_with_text_html_content_type_and_body()
    {
        // HtmlWidget returns SliceResult.Html("<h1>Hello from SliceFx</h1>").
        // Without the wrapper the struct would be JSON-serialized; with it we get text/html.
        await using var host = SliceTestHost.Create<SliceApp::Program>();

        using var response = await host.Client.GetAsync("/widgets/html", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("<h1>Hello from SliceFx</h1>", body);
    }
}
