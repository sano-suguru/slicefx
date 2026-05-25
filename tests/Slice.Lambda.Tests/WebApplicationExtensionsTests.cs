namespace Slice.Lambda.Tests;

public sealed class WebApplicationExtensionsTests
{
    [Fact]
    public async Task RunOnLambdaAsync_throws_for_null_app()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            WebApplicationExtensions.RunOnLambdaAsync(null!));
    }
}
