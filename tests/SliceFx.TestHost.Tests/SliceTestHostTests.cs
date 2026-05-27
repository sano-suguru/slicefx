using Microsoft.Extensions.DependencyInjection;
using SliceFx.TestHost.TestApp;
using SliceFx.Testing;

namespace SliceFx.TestHost.Tests;

public sealed class SliceTestHostTests
{
    [Fact]
    public async Task Create_starts_host_and_exposes_client_and_services()
    {
        await using var host = SliceTestHost.Create<Program>();

        var message = await host.Client.GetStringAsync("/message", TestContext.Current.CancellationToken);

        Assert.Equal("app", message);
        Assert.IsType<DefaultMessageService>(host.Services.GetRequiredService<IMessageService>());
    }

    [Fact]
    public async Task Create_applies_configure_after_app_services()
    {
        await using var host = SliceTestHost.Create<Program>(services =>
            services.Replace<IMessageService>(new ReplacementMessageService()));

        var message = await host.Client.GetStringAsync("/message", TestContext.Current.CancellationToken);

        Assert.Equal("test", message);
    }

    [Fact]
    public async Task Create_applies_content_root()
    {
        var contentRoot = Directory.CreateTempSubdirectory("slice-testhost-").FullName;
        try
        {
            await using var host = SliceTestHost.Create<Program>(contentRoot: contentRoot);

            var observed = await host.Client.GetStringAsync("/content-root", TestContext.Current.CancellationToken);

            Assert.Equal(contentRoot, observed);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }
}
