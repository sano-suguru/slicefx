using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Slice.Testing;

/// <summary>
/// Convenience extensions for replacing service registrations in a test host.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Removes any existing registration for <typeparamref name="TService"/> and adds
    /// <typeparamref name="TImplementation"/> with the given <paramref name="lifetime"/>.
    /// </summary>
    public static IServiceCollection Replace<TService, TImplementation>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TService : class
        where TImplementation : class, TService
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<TService>();
        services.Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), lifetime));
        return services;
    }

    /// <summary>
    /// Removes any existing registration for <typeparamref name="TService"/> and adds
    /// <paramref name="instance"/> as a singleton.
    /// </summary>
    public static IServiceCollection Replace<TService>(
        this IServiceCollection services,
        TService instance)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(instance);

        services.RemoveAll<TService>();
        services.AddSingleton(instance);
        return services;
    }
}
