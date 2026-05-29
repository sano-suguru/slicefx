namespace SliceFx.Wasi.Spin;

/// <summary>
/// Describes a single Spin cron trigger invocation passed to <see cref="ISpinCronHandler.OnTickAsync"/>.
/// </summary>
/// <param name="FireTime">The UTC time at which the cron trigger fired.</param>
public sealed record SpinCronContext(DateTimeOffset FireTime);
