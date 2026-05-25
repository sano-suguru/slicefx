using Microsoft.Extensions.DependencyInjection;

namespace Slice.Lambda.PerFunction.Tests;

public sealed class LambdaServiceProviderTests
{
    [Fact]
    public void Build_applies_configure_callback()
    {
        using var provider = LambdaServiceProvider.Build(static services =>
            services.AddSingleton<IConfiguredService, ConfiguredService>());

        Assert.IsType<ConfiguredService>(provider.GetRequiredService<IConfiguredService>());
    }

    [Fact]
    public void Build_validates_services_at_build_time()
    {
        var exception = Assert.Throws<AggregateException>(() =>
            LambdaServiceProvider.Build(static services =>
                services.AddSingleton<NeedsMissingDependency>()));

        Assert.Contains(exception.InnerExceptions, static inner =>
            inner is InvalidOperationException &&
            inner.Message.Contains(nameof(IMissingDependency), StringComparison.Ordinal));
    }

    private interface IConfiguredService;

    private sealed class ConfiguredService : IConfiguredService;

    private interface IMissingDependency;

    private sealed class NeedsMissingDependency(IMissingDependency dependency)
    {
        public IMissingDependency Dependency { get; } = dependency;
    }
}
