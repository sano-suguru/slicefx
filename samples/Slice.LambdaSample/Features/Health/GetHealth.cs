namespace Slice.LambdaSample.Features.Health;

[Feature("GET /health", Summary = "Health check")]
public static class GetHealth
{
    public record Response(string Status, DateTime Timestamp);

    public static IResult Handle()
        => Results.Ok(new Response("ok", DateTime.UtcNow));
}
