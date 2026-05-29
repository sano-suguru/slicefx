namespace SliceFx.Wasi.Spin;

/// <summary>
/// Describes a single Spin cron trigger invocation passed to <see cref="ISpinCronHandler.OnTickAsync"/>.
/// </summary>
/// <param name="FireTime">The UTC time at which the cron trigger fired.</param>
/// <param name="Metadata">Optional metadata string from the cron trigger definition; <c>null</c> if not provided.</param>
public sealed record SpinCronContext(DateTimeOffset FireTime, string? Metadata = null);
