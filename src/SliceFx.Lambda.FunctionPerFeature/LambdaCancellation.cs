using Amazon.Lambda.Core;

namespace SliceFx.Lambda.FunctionPerFeature;

/// <summary>
/// Creates cancellation sources for Lambda invocations.
/// </summary>
public static class LambdaCancellation
{
    private static readonly TimeSpan s_defaultBuffer = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Creates a cancellation token source that cancels shortly before the Lambda timeout.
    /// </summary>
    /// <param name="context">The current Lambda context.</param>
    /// <returns>A cancellation token source for the current invocation.</returns>
    public static CancellationTokenSource Create(ILambdaContext context)
        => Create(context, TimeProvider.System);

    /// <summary>
    /// Creates a cancellation token source that cancels shortly before the Lambda timeout.
    /// </summary>
    /// <param name="context">The current Lambda context.</param>
    /// <param name="timeProvider">The clock used to schedule cancellation.</param>
    /// <returns>A cancellation token source for the current invocation.</returns>
    public static CancellationTokenSource Create(ILambdaContext context, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var remaining = context.RemainingTime - s_defaultBuffer;
        if (remaining > TimeSpan.Zero)
        {
            return new CancellationTokenSource(remaining, timeProvider);
        }

        var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts;
    }
}
