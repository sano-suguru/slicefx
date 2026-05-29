namespace SliceFx.Wasi.Spin.Tests;

public sealed class RecordingSpinCronHandlerTests
{
    private static readonly DateTimeOffset FixedFireTime = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OnTickAsync_SingleInvocation_RecordsContext()
    {
        var handler = new RecordingSpinCronHandler();
        var context = new SpinCronContext(FixedFireTime, "test");

        await handler.OnTickAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(handler.Invocations);
        Assert.Same(context, handler.Invocations[0]);
    }

    [Fact]
    public async Task OnTickAsync_MultipleInvocations_RecordsAll()
    {
        var handler = new RecordingSpinCronHandler();
        var ctx1 = new SpinCronContext(FixedFireTime);
        var ctx2 = new SpinCronContext(FixedFireTime.AddMinutes(1));

        await handler.OnTickAsync(ctx1, TestContext.Current.CancellationToken);
        await handler.OnTickAsync(ctx2, TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Invocations.Count);
        Assert.Same(ctx1, handler.Invocations[0]);
        Assert.Same(ctx2, handler.Invocations[1]);
    }

    [Fact]
    public async Task Clear_RemovesAllInvocations()
    {
        var handler = new RecordingSpinCronHandler();
        await handler.OnTickAsync(new SpinCronContext(FixedFireTime), TestContext.Current.CancellationToken);
        handler.Clear();

        Assert.Empty(handler.Invocations);
    }

    [Fact]
    public async Task OnTickAsync_NullContext_Throws()
    {
        var handler = new RecordingSpinCronHandler();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => handler.OnTickAsync(null!, TestContext.Current.CancellationToken).AsTask());
    }
}
