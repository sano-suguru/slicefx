namespace SliceFx.Wasi.Spin.Tests;

public sealed class SpinCronDispatcherTests
{
    private static readonly DateTimeOffset FixedFireTime = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DispatchAsync_CallsOnTickAsync_Once()
    {
        var handler = new RecordingSpinCronHandler();
        var app = WasiHost.CreateBuilder()
            .AddSpinCronHandler(handler)
            .Build();
        var context = new SpinCronContext(FixedFireTime);

        await SpinCronDispatcher.DispatchAsync(app, context, TestContext.Current.CancellationToken);

        Assert.Single(handler.Invocations);
    }

    [Fact]
    public async Task DispatchAsync_PassesContextTransparently()
    {
        var handler = new RecordingSpinCronHandler();
        var app = WasiHost.CreateBuilder()
            .AddSpinCronHandler(handler)
            .Build();
        var context = new SpinCronContext(FixedFireTime, "meta=42");

        await SpinCronDispatcher.DispatchAsync(app, context, TestContext.Current.CancellationToken);

        Assert.Equal(FixedFireTime, handler.Invocations[0].FireTime);
        Assert.Equal("meta=42", handler.Invocations[0].Metadata);
    }

    [Fact]
    public async Task DispatchAsync_NoHandlerRegistered_Throws()
    {
        var app = WasiHost.CreateBuilder().Build();
        var context = new SpinCronContext(FixedFireTime);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SpinCronDispatcher.DispatchAsync(app, context, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task DispatchAsync_NullApp_Throws()
    {
        var context = new SpinCronContext(FixedFireTime);
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => SpinCronDispatcher.DispatchAsync(null!, context, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task DispatchAsync_NullContext_Throws()
    {
        var app = WasiHost.CreateBuilder()
            .AddSpinCronHandler(new RecordingSpinCronHandler())
            .Build();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => SpinCronDispatcher.DispatchAsync(app, null!, TestContext.Current.CancellationToken).AsTask());
    }
}
