namespace SliceFx.Wasi.Spin;

/// <summary>
/// Recording <see cref="ISpinCronHandler"/> implementation for unit tests and local development.
/// Each <see cref="OnTickAsync"/> invocation appends its <see cref="SpinCronContext"/> to <see cref="Invocations"/>.
/// Call <see cref="Clear"/> to reset state between tests.
/// </summary>
public sealed class RecordingSpinCronHandler : ISpinCronHandler
{
    private readonly List<SpinCronContext> _invocations = [];

    /// <summary>Gets the ordered list of <see cref="SpinCronContext"/> values received so far.</summary>
    public IReadOnlyList<SpinCronContext> Invocations => _invocations;

    /// <summary>
    /// Records <paramref name="context"/> in <see cref="Invocations"/> and completes immediately.
    /// </summary>
    public ValueTask OnTickAsync(SpinCronContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        _invocations.Add(context);
        return ValueTask.CompletedTask;
    }

    /// <summary>Removes all recorded invocations. Useful for resetting state between tests.</summary>
    public void Clear() => _invocations.Clear();
}
