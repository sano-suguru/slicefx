using Slice.Workers;
using Slice.WorkersSample.Services;

namespace Slice.WorkersSample.Features.Health;

[Feature("GET /health", Summary = "Worker health check")]
public static class GetHealth
{
    public record Response(string Status, DateTimeOffset Timestamp);

    public static WorkerResponse Handle(IClock clock)
        => SliceResult.Ok(new Response("ok", clock.UtcNow));
}
