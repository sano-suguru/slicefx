using Amazon.Lambda.Core;

namespace Slice.Lambda.PerFunction;

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
    {
        ArgumentNullException.ThrowIfNull(context);

        var remaining = context.RemainingTime - s_defaultBuffer;
        var cts = new CancellationTokenSource();
        if (remaining > TimeSpan.Zero)
        {
            cts.CancelAfter(remaining);
        }
        else
        {
            cts.Cancel();
        }

        return cts;
    }
}
