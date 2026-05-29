using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Wasi.Spin;

/// <summary>
/// Extension methods for registering <see cref="ISpinCronHandler"/> and <see cref="ISpinVariables"/>
/// implementations with <see cref="WasiHostBuilder"/>.
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

    /// <summary>
    /// Registers <paramref name="variables"/> as the singleton <see cref="ISpinVariables"/> for the WASI application.
    /// </summary>
    public static WasiHostBuilder AddSpinVariables(this WasiHostBuilder builder, ISpinVariables variables)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(variables);
        builder.Services.AddSingleton(variables);
        return builder;
    }

    /// <summary>
    /// Registers <typeparamref name="TVariables"/> as the singleton <see cref="ISpinVariables"/> for the WASI application.
    /// </summary>
    public static WasiHostBuilder AddSpinVariables<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TVariables>(this WasiHostBuilder builder)
        where TVariables : class, ISpinVariables
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<ISpinVariables, TVariables>();
        return builder;
    }
}
