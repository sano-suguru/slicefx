namespace Slice.Lambda.PerFunction.Tests;

public sealed class LambdaCancellationTests
{
    [Fact]
    public void Create_throws_for_null_context()
        => Assert.Throws<ArgumentNullException>(() => LambdaCancellation.Create(null!));

    [Fact]
    public void Create_throws_for_null_time_provider()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LambdaCancellation.Create(new TestLambdaContext(TimeSpan.FromSeconds(1)), null!));
    }

    [Fact]
    public void Create_cancels_immediately_when_remaining_time_is_within_buffer()
    {
        using var cts = LambdaCancellation.Create(
            new TestLambdaContext(TimeSpan.FromMilliseconds(500)),
            new ManualTimeProvider());

        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Create_schedules_cancellation_before_timeout()
    {
        var timeProvider = new ManualTimeProvider();
        using var cts = LambdaCancellation.Create(
            new TestLambdaContext(TimeSpan.FromSeconds(1)),
            timeProvider);

        Assert.False(cts.IsCancellationRequested);
        Assert.Equal(TimeSpan.FromMilliseconds(500), timeProvider.DueTime);

        timeProvider.FireAll();

        Assert.True(cts.IsCancellationRequested);
    }
}
