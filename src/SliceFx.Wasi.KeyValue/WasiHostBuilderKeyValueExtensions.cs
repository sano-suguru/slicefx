using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Wasi.KeyValue;

/// <summary>
/// Extension methods for registering <see cref="IKeyValueStore"/> implementations with <see cref="WasiHostBuilder"/>.
/// </summary>
public static class WasiHostBuilderKeyValueExtensions
{
    /// <summary>
    /// Registers <paramref name="store"/> as the singleton <see cref="IKeyValueStore"/> for the WASI application.
    /// </summary>
    public static WasiHostBuilder AddKeyValueStore(this WasiHostBuilder builder, IKeyValueStore store)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);
        builder.Services.AddSingleton(store);
        return builder;
    }

    /// <summary>
    /// Registers <typeparamref name="TStore"/> as the singleton <see cref="IKeyValueStore"/> for the WASI application.
    /// </summary>
    public static WasiHostBuilder AddKeyValueStore<TStore>(this WasiHostBuilder builder)
        where TStore : class, IKeyValueStore
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IKeyValueStore, TStore>();
        return builder;
    }
}
