using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Testing;

/// <summary>
/// Factory for creating in-process test hosts backed by <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// </summary>
public static class SliceTestHost
{
    /// <summary>
    /// Creates a started test host for <typeparamref name="TEntryPoint"/>.
    /// The optional <paramref name="configure"/> callback runs after the application's own
    /// service registrations, allowing services to be replaced for testing.
    /// </summary>
    /// <param name="configure">Override DI registrations for test isolation.</param>
    /// <param name="contentRoot">
    /// Optional content root for the in-process server. When omitted,
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> uses its normal discovery behavior.
    /// Pass the application's project directory when the app reads static files or configuration
    /// from its content root and discovery is not sufficient.
    /// </param>
    /// <typeparam name="TEntryPoint">The application entry point type used by ASP.NET Core's test host.</typeparam>
    /// <returns>A started in-process Slice test host.</returns>
    public static SliceTestHost<TEntryPoint> Create<TEntryPoint>(
        Action<IServiceCollection>? configure = null,
        string? contentRoot = null)
        where TEntryPoint : class
        => SliceTestHost<TEntryPoint>.Create(configure, contentRoot);
}

/// <summary>
/// An in-process test host wrapping a running instance of the application.
/// Dispose via <c>await using</c> to shut down the host cleanly.
/// </summary>
/// <typeparam name="TEntryPoint">The application entry point type used by ASP.NET Core's test host.</typeparam>
public sealed class SliceTestHost<TEntryPoint> : IAsyncDisposable
    where TEntryPoint : class
{
    private readonly SliceWebApplicationFactory<TEntryPoint> _factory;

    private SliceTestHost(SliceWebApplicationFactory<TEntryPoint> factory)
    {
        _factory = factory;
        // CreateClient() triggers host startup; Services is available after this call.
        Client = factory.CreateClient();
        Services = factory.Services;
    }

    /// <summary>An <see cref="HttpClient"/> pre-configured to reach the test server.</summary>
    public HttpClient Client { get; }

    /// <summary>The test server's root <see cref="IServiceProvider"/>.</summary>
    public IServiceProvider Services { get; }

    internal static SliceTestHost<TEntryPoint> Create(
        Action<IServiceCollection>? configure,
        string? contentRoot)
    {
        var factory = new SliceWebApplicationFactory<TEntryPoint>(
            configure ?? (_ => { }),
            contentRoot);
        return new SliceTestHost<TEntryPoint>(factory);
    }

    /// <summary>
    /// Disposes the HTTP client and underlying web application factory.
    /// </summary>
    /// <returns>A task that completes when the host has shut down.</returns>
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class SliceWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    private readonly Action<IServiceCollection> _configure;
    private readonly string? _contentRoot;

    internal SliceWebApplicationFactory(Action<IServiceCollection> configure, string? contentRoot)
    {
        _configure = configure;
        _contentRoot = contentRoot;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_contentRoot is not null)
        {
            builder.UseContentRoot(_contentRoot);
        }

        builder.ConfigureTestServices(_configure);
    }
}
