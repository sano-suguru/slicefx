namespace SliceFx.WasiSample.Filters;

/// <summary>
/// A host-neutral request timing filter that observes the HTTP status of the inner pipeline
/// after the handler completes. Demonstrates the pass-through / post-handler observation pattern.
/// </summary>
/// <remarks>
/// This filter does not short-circuit; it always calls <c>next</c> and observes the result.
/// In a real application you would record the elapsed time to a metrics system.
/// </remarks>
/// <param name="timeProvider">Clock service resolved from DI for timing measurements.</param>
public sealed class RequestTimingFilter(TimeProvider timeProvider) : ISliceFilter
{
    /// <inheritdoc />
    public async ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
    {
        var start = timeProvider.GetTimestamp();
        var result = await next(context).ConfigureAwait(false);
        var elapsed = timeProvider.GetElapsedTime(start);

        // Observing the status is best-effort: it is always present on the WASI path,
        // and present when the ASP.NET handler returns SliceResult or IStatusCodeHttpResult.
        var status = result.Status?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?";
        Console.Error.WriteLine($"[timing] {context.Method} {context.Path} → {status} in {elapsed.TotalMilliseconds:F1}ms");

        return result;
    }
}
