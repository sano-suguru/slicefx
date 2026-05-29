using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Wasi.Spin;

/// <summary>
/// In-process bridge that resolves the registered <see cref="ISpinCronHandler"/> and dispatches a cron tick.
/// </summary>
/// <remarks>
/// Call <see cref="DispatchAsync"/> from your Spin cron export entry point, analogous to calling
/// <c>WasiApp.DispatchAsync</c> from the <c>wasi:http/incoming-handler</c> export.
/// </remarks>
public static class SpinCronDispatcher
{
    /// <summary>
    /// Resolves the <see cref="ISpinCronHandler"/> registered in <paramref name="app"/> and invokes
    /// <see cref="ISpinCronHandler.OnTickAsync"/> with <paramref name="context"/>.
    /// </summary>
    /// <param name="app">The built <see cref="WasiApp"/> whose DI container holds the handler.</param>
    /// <param name="context">Context describing the cron trigger invocation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <see cref="ISpinCronHandler"/> has been registered — call
    /// <see cref="WasiHostBuilderSpinExtensions.AddSpinCronHandler{THandler}"/> during startup.
    /// </exception>
    public static ValueTask DispatchAsync(WasiApp app, SpinCronContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(context);
        var handler = app.Services.GetRequiredService<ISpinCronHandler>();
        return handler.OnTickAsync(context, ct);
    }
}
