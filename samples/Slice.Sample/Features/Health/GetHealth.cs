namespace Slice.Sample.Features.Health;

/// <summary>
/// Implements the sample health endpoint at <c>GET /health</c>.
/// </summary>
[Feature("GET /health", Summary = "Health check")]
public static class GetHealth
{
    /// <summary>
    /// Health payload returned by the sample endpoint.
    /// </summary>
    /// <param name="Status">Current health status, for example <c>ok</c>.</param>
    /// <param name="Timestamp">UTC time when the response was created.</param>
    public record Response(string Status, DateTime Timestamp);

    /// <summary>
    /// Returns a lightweight health response for smoke tests.
    /// </summary>
    /// <param name="timeProvider">Clock service resolved from the sample host.</param>
    /// <returns>The current health status.</returns>
    public static Response Handle(TimeProvider timeProvider)
        => new("ok", timeProvider.GetUtcNow().UtcDateTime);
}
