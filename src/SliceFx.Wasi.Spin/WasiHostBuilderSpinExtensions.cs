using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Wasi.Spin;

/// <summary>
/// Extension methods for registering <see cref="ISpinCronHandler"/> implementations with <see cref="WasiHostBuilder"/>.
/// </summary>
public static class WasiHostBuilderSpinExtensions
{
    /// <summary>
    /// Registers <paramref name="handler"/> as the singleton <see cref="ISpinCronHandler"/> for the WASI application.
    /// </summary>
    public static WasiHostBuilder AddSpinCronHandler(this WasiHostBuilder builder, ISpinCronHandler handler)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(handler);
        builder.Services.AddSingleton(handler);
        return builder;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the singleton <see cref="ISpinCronHandler"/> for the WASI application.
    /// </summary>
    public static WasiHostBuilder AddSpinCronHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this WasiHostBuilder builder)
        where THandler : class, ISpinCronHandler
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<ISpinCronHandler, THandler>();
        return builder;
    }
}
