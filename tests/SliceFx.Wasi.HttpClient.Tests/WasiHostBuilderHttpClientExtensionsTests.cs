using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Wasi.HttpClient.Tests;

public sealed class WasiHostBuilderHttpClientExtensionsTests
{
    [Fact]
    public void AddWasiHttpClient_Instance_RegistersAsSingleton()
    {
        var builder = WasiHost.CreateBuilder();
        var client = new InMemoryWasiHttpClient();
        builder.AddWasiHttpClient(client);
        var app = builder.Build();
        var resolved = app.Services.GetRequiredService<IWasiHttpClient>();
        Assert.Same(client, resolved);
    }

    [Fact]
    public void AddWasiHttpClient_Generic_RegistersAsSingleton()
    {
        var builder = WasiHost.CreateBuilder();
        builder.AddWasiHttpClient<InMemoryWasiHttpClient>();
        var app = builder.Build();
        var resolved = app.Services.GetRequiredService<IWasiHttpClient>();
        Assert.IsType<InMemoryWasiHttpClient>(resolved);
    }

    [Fact]
    public void AddWasiHttpClient_ReturnsSameBuilderForChaining()
    {
        var builder = WasiHost.CreateBuilder();
        var returned = builder.AddWasiHttpClient(new InMemoryWasiHttpClient());
        Assert.Same(builder, returned);
    }

    [Fact]
    public void AddWasiHttpClient_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WasiHostBuilderHttpClientExtensions.AddWasiHttpClient(null!, new InMemoryWasiHttpClient()));
    }

    [Fact]
    public void AddWasiHttpClient_NullClient_Throws()
    {
        var builder = WasiHost.CreateBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.AddWasiHttpClient(null!));
    }
}
