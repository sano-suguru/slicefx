using System.Net;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Benchmarks.Runtime;

/// <summary>
/// Adds the full JSON exporter so the perf workflow can parse measurements
/// and assert against gate values in <c>tests/SliceFx.Benchmarks.Runtime/runtime-gates.json</c>.
/// </summary>
internal sealed class JsonReportConfig : ManualConfig
{
    /// <summary>
    /// Initializes the configuration with the full JSON exporter attached.
    /// </summary>
    public JsonReportConfig() => AddExporter(JsonExporter.Full);
}

/// <summary>
/// Benchmarks SliceFx runtime performance: host startup cost and per-request dispatch latency.
/// <list type="bullet">
///   <item><c>BuildHostOnly</c> — AddSlice + DI container Build, no routing; the host-init baseline.</item>
///   <item><c>Startup</c> — end-to-end ceiling: BuildHostOnly + MapSlices + endpoint materialization.</item>
///   <item><c>Startup − BuildHostOnly</c> — approximates the SliceFx routing and RDF cost.</item>
///   <item><c>Request_*</c> — per-request round-trip latency via in-memory TestServer.</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
[Config(typeof(JsonReportConfig))]
public class RuntimeBenchmarks
{
    private WebApplication _requestApp = null!;
    private HttpClient _client = null!;

    /// <summary>
    /// Number of registered feature routes for the current benchmark run.
    /// </summary>
    [Params(50, 100, 200)]
    public int FeatureCount { get; set; }

    /// <summary>
    /// Starts a single in-memory host used by all <c>Request_*</c> benchmarks and validates
    /// that both probe endpoints return 2xx so benchmarks measure the intended success hot path.
    /// </summary>
    [GlobalSetup]
    public async Task Setup()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        AddServices(builder.Services);
        _requestApp = builder.Build();
        MapRoutes(_requestApp);
        await _requestApp.StartAsync();
        var testServer = (TestServer)_requestApp.Services.GetRequiredService<IServer>();
        _client = testServer.CreateClient();

        using var getProbe = await _client.GetAsync("/bench/s/f0");
        getProbe.EnsureSuccessStatusCode();
        using var postProbe = await _client.PostAsync("/bench/v/f0", CreateValidPostBody());
        postProbe.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Stops the in-memory host and releases resources.
    /// </summary>
    [GlobalCleanup]
    public async Task Cleanup()
    {
        _client?.Dispose();
        if (_requestApp is not null)
        {
            await _requestApp.StopAsync();
            await _requestApp.DisposeAsync();
        }
    }

    /// <summary>
    /// Measures the in-process cost of AddSlice + DI container Build without MapSlices or endpoint
    /// materialization. Use as a baseline: <c>Startup − BuildHostOnly</c> approximates the
    /// SliceFx routing and RequestDelegateFactory cost for the current <see cref="FeatureCount"/>.
    /// </summary>
    /// <returns>DI container hash code (prevents DCE).</returns>
    [Benchmark]
    public async Task<int> BuildHostOnly()
    {
        var builder = WebApplication.CreateSlimBuilder();
        AddServices(builder.Services);
        await using var app = builder.Build();
        return app.Services.GetHashCode();
    }

    /// <summary>
    /// Measures the in-process cost of AddSlice + Build + MapSlices + endpoint materialization
    /// for the current <see cref="FeatureCount"/>. Forces eager endpoint-pipeline construction
    /// by enumerating <c>DataSources[*].Endpoints</c> before dispose.
    /// </summary>
    /// <returns>Number of materialized endpoints (prevents DCE).</returns>
    [Benchmark]
    public async Task<int> Startup()
    {
        var builder = WebApplication.CreateSlimBuilder();
        AddServices(builder.Services);
        await using var app = builder.Build();
        MapRoutes(app);
        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static d => d.Endpoints)
            .Count();
    }

    /// <summary>
    /// Measures round-trip latency for a zero-body GET request dispatched through the generated route table.
    /// </summary>
    /// <returns>HTTP status code (prevents DCE).</returns>
    [Benchmark]
    public async Task<HttpStatusCode> Request_SimpleGet()
    {
        using var response = await _client.GetAsync("/bench/s/f0");
        return response.StatusCode;
    }

    /// <summary>
    /// Measures round-trip latency for a POST request that passes DataAnnotations validation
    /// through the source-generated validation pipeline.
    /// </summary>
    /// <returns>HTTP status code (prevents DCE).</returns>
    [Benchmark]
    public async Task<HttpStatusCode> Request_PostWithValidation()
    {
        using var content = CreateValidPostBody();
        using var response = await _client.PostAsync("/bench/v/f0", content);
        return response.StatusCode;
    }

    private void AddServices(IServiceCollection services)
    {
        switch (FeatureCount)
        {
            case 50: Bench50.BenchHost.Add(services); break;
            case 100: Bench100.BenchHost.Add(services); break;
            case 200: Bench200.BenchHost.Add(services); break;
            default: throw new InvalidOperationException($"Unsupported FeatureCount: {FeatureCount}");
        }
    }

    private void MapRoutes(IEndpointRouteBuilder endpoints)
    {
        switch (FeatureCount)
        {
            case 50: Bench50.BenchHost.Map(endpoints); break;
            case 100: Bench100.BenchHost.Map(endpoints); break;
            case 200: Bench200.BenchHost.Map(endpoints); break;
            default: throw new InvalidOperationException($"Unsupported FeatureCount: {FeatureCount}");
        }
    }

    private static StringContent CreateValidPostBody() =>
        new(/*lang=json,strict*/ """{"name":"Alice","email":"alice@example.com"}""", Encoding.UTF8, "application/json");
}
