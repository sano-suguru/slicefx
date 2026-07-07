namespace SliceFx.WasiSample.Tests;

public sealed class SmokeTests
{
    private static WasiRequest Get(string path) =>
        new("GET", path, new Dictionary<string, string>(), QueryString: null, Body: null);

    [Fact]
    public async Task Factory_builds_and_dispatches_health_route()
    {
        await using var app = SampleWasiApp.Create();

        var response = await app.DispatchAsync(Get("/health"), TestContext.Current.CancellationToken);

        Assert.Equal(200, response.Status);
    }
}
