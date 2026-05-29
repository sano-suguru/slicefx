using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Wasi.KeyValue.Tests;

public sealed class WasiHostBuilderKeyValueExtensionsTests
{
    [Fact]
    public void AddKeyValueStore_Instance_RegistersAsSingleton()
    {
        var builder = WasiHost.CreateBuilder();
        var store = new InMemoryKeyValueStore();
        builder.AddKeyValueStore(store);
        var app = builder.Build();
        var resolved = app.Services.GetRequiredService<IKeyValueStore>();
        Assert.Same(store, resolved);
    }

    [Fact]
    public void AddKeyValueStore_Generic_RegistersAsSingleton()
    {
        var builder = WasiHost.CreateBuilder();
        builder.AddKeyValueStore<InMemoryKeyValueStore>();
        var app = builder.Build();
        var resolved = app.Services.GetRequiredService<IKeyValueStore>();
        Assert.IsType<InMemoryKeyValueStore>(resolved);
    }

    [Fact]
    public void AddKeyValueStore_ReturnsSameBuilderForChaining()
    {
        var builder = WasiHost.CreateBuilder();
        var returned = builder.AddKeyValueStore(new InMemoryKeyValueStore());
        Assert.Same(builder, returned);
    }
}
