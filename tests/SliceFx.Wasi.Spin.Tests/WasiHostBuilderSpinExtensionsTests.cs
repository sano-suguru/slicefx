using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Wasi.Spin.Tests;

public sealed class WasiHostBuilderSpinExtensionsTests
{
    [Fact]
    public void AddSpinCronHandler_Instance_RegistersAsSingleton()
    {
        var builder = WasiHost.CreateBuilder();
        var handler = new RecordingSpinCronHandler();
        builder.AddSpinCronHandler(handler);
        var app = builder.Build();
        var resolved = app.Services.GetRequiredService<ISpinCronHandler>();
        Assert.Same(handler, resolved);
    }

    [Fact]
    public void AddSpinCronHandler_Generic_RegistersAsSingleton()
    {
        var builder = WasiHost.CreateBuilder();
        builder.AddSpinCronHandler<RecordingSpinCronHandler>();
        var app = builder.Build();
        var resolved = app.Services.GetRequiredService<ISpinCronHandler>();
        Assert.IsType<RecordingSpinCronHandler>(resolved);
    }

    [Fact]
    public void AddSpinCronHandler_ReturnsSameBuilderForChaining()
    {
        var builder = WasiHost.CreateBuilder();
        var returned = builder.AddSpinCronHandler(new RecordingSpinCronHandler());
        Assert.Same(builder, returned);
    }

    [Fact]
    public void AddSpinCronHandler_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WasiHostBuilderSpinExtensions.AddSpinCronHandler(null!, new RecordingSpinCronHandler()));
    }

    [Fact]
    public void AddSpinCronHandler_NullHandler_Throws()
    {
        var builder = WasiHost.CreateBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.AddSpinCronHandler(null!));
    }

    [Fact]
    public void AddSpinVariables_Instance_RegistersAsSingleton()
    {
        var builder = WasiHost.CreateBuilder();
        var variables = new InMemorySpinVariables();
        builder.AddSpinVariables(variables);
        var app = builder.Build();
        var resolved = app.Services.GetRequiredService<ISpinVariables>();
        Assert.Same(variables, resolved);
    }

    [Fact]
    public void AddSpinVariables_Generic_RegistersAsSingleton()
    {
        var builder = WasiHost.CreateBuilder();
        builder.AddSpinVariables<InMemorySpinVariables>();
        var app = builder.Build();
        var resolved = app.Services.GetRequiredService<ISpinVariables>();
        Assert.IsType<InMemorySpinVariables>(resolved);
    }

    [Fact]
    public void AddSpinVariables_ReturnsSameBuilderForChaining()
    {
        var builder = WasiHost.CreateBuilder();
        var returned = builder.AddSpinVariables(new InMemorySpinVariables());
        Assert.Same(builder, returned);
    }

    [Fact]
    public void AddSpinVariables_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WasiHostBuilderSpinExtensions.AddSpinVariables(null!, new InMemorySpinVariables()));
    }

    [Fact]
    public void AddSpinVariables_NullVariables_Throws()
    {
        var builder = WasiHost.CreateBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.AddSpinVariables(null!));
    }
}
