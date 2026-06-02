using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Wasi.HttpClient;

/// <summary>
/// Extension methods for registering <see cref="IWasiHttpClient"/> implementations with <see cref="WasiHostBuilder"/>.
/// </summary>
public static class WasiHostBuilderHttpClientExtensions
{
    /// <summary>
    /// Registers <paramref name="client"/> as the singleton <see cref="IWasiHttpClient"/> for the WASI application.
    /// </summary>
    public static WasiHostBuilder AddWasiHttpClient(this WasiHostBuilder builder, IWasiHttpClient client)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);
        builder.Services.AddSingleton(client);
        return builder;
    }

    /// <summary>
    /// Registers <typeparamref name="TClient"/> as the singleton <see cref="IWasiHttpClient"/> for the WASI application.
    /// </summary>
    /// <remarks>
    /// This overload uses reflection-based constructor activation and is not NativeAOT/trim-safe.
    /// In NativeAOT or trim-enabled builds (e.g. WASI publish), prefer the instance overload:
    /// <c>builder.AddWasiHttpClient(new TClient(...))</c>.
    /// </remarks>
    [RequiresUnreferencedCode("Uses reflection to activate TClient. Use the instance overload in trim/NativeAOT builds.")]
    [RequiresDynamicCode("Uses dynamic code to activate TClient. Use the instance overload in NativeAOT builds.")]
    public static WasiHostBuilder AddWasiHttpClient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(this WasiHostBuilder builder)
        where TClient : class, IWasiHttpClient
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IWasiHttpClient, TClient>();
        return builder;
    }
}
