namespace Slice.LambdaSample.Features.Health;

/// <summary>
/// Implements the Lambda sample health endpoint at <c>GET /health</c>.
/// </summary>
[Feature("GET /health", Summary = "Health check")]
public static class GetHealth
{
    /// <summary>
    /// Health payload returned by the Lambda sample.
    /// </summary>
    /// <param name="Status">Current health status, for example <c>ok</c>.</param>
    /// <param name="Timestamp">UTC time when the response was created.</param>
    public record Response(string Status, DateTime Timestamp);

    /// <summary>
    /// Returns a lightweight health response.
    /// </summary>
    /// <returns>An HTTP 200 response containing the health payload.</returns>
    public static IResult Handle()
        => Results.Ok(new Response("ok", DateTime.UtcNow));
}
