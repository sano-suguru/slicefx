namespace Slice.Sample.Features.Health;

[Feature("GET /health", Summary = "Health check")]
public static class GetHealth
{
    public record Response(string Status, DateTime Timestamp);

    public static Response Handle()
        => new("ok", DateTime.UtcNow);
}
