using Microsoft.Extensions.DependencyInjection;
using Slice.TestHost.TestApp;
using Slice.Testing;

namespace Slice.TestHost.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void Replace_implementation_throws_for_null_services()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ServiceCollectionExtensions.Replace<IMessageService, ReplacementMessageService>(null!));
    }

    [Fact]
    public void Replace_implementation_removes_existing_registrations_and_adds_requested_lifetime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageService, DefaultMessageService>();
        services.AddScoped<IMessageService, OtherMessageService>();

        var returned = services.Replace<IMessageService, ReplacementMessageService>(ServiceLifetime.Scoped);

        Assert.Same(services, returned);
        var descriptor = Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IMessageService));
        Assert.Equal(typeof(ReplacementMessageService), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void Replace_instance_throws_for_null_arguments()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            ServiceCollectionExtensions.Replace<IMessageService>(null!, new ReplacementMessageService()));
        Assert.Throws<ArgumentNullException>(() =>
            services.Replace<IMessageService>(null!));
    }

    [Fact]
    public void Replace_instance_removes_existing_registrations_and_adds_singleton_instance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageService, DefaultMessageService>();
        var replacement = new ReplacementMessageService();

        services.Replace<IMessageService>(replacement);
        services.Replace<IMessageService>(replacement);

        var descriptor = Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IMessageService));
        Assert.Same(replacement, descriptor.ImplementationInstance);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
