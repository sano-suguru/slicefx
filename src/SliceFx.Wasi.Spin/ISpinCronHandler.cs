namespace SliceFx.Wasi.Spin;

/// <summary>
/// Abstraction over a Spin cron trigger handler for use in SliceFx WASI applications.
/// </summary>
/// <remarks>
/// Implement this interface to handle periodic Spin cron trigger invocations.
/// Register a concrete implementation with <see cref="WasiHostBuilderSpinExtensions.AddSpinCronHandler{THandler}"/>
/// or the instance overload, then call <see cref="SpinCronDispatcher.DispatchAsync"/> from your
/// cron export entry point. Use <see cref="RecordingSpinCronHandler"/> in unit tests.
/// </remarks>
public interface ISpinCronHandler
{
    /// <summary>Handles a single Spin cron trigger invocation.</summary>
    /// <param name="context">Context describing the cron trigger invocation, including fire time.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask OnTickAsync(SpinCronContext context, CancellationToken ct = default);
}
