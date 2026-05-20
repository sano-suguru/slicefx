using Slice.WorkersSample.Services;

namespace Slice.WorkersSample.Features.Health;

[Feature("GET /health", Summary = "Worker health check")]
public static class GetHealth
{
    public record Response(string Status, DateTimeOffset Timestamp);

    public static Response Handle(IClock clock)
        => new("ok", clock.UtcNow);
}
