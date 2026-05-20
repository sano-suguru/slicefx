using Slice.WorkersSample.Services;

namespace Slice.WorkersSample.Features.Health;

/// <summary>
/// Provides a lightweight Workers health check.
/// </summary>
[Feature("GET /health", Summary = "Worker health check")]
public static class GetHealth
{
    /// <summary>
    /// Health payload returned by the Workers sample.
    /// </summary>
    /// <param name="Status">Current health status, for example <c>ok</c>.</param>
    /// <param name="Timestamp">UTC time provided by the sample clock service.</param>
    public record Response(string Status, DateTimeOffset Timestamp);

    /// <summary>
    /// Returns the current health state for in-process probes.
    /// </summary>
    /// <param name="clock">Clock service resolved from the Workers host.</param>
    /// <returns>The health response payload.</returns>
    public static Response Handle(IClock clock)
        => new("ok", clock.UtcNow);
}
