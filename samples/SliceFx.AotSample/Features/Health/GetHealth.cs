namespace SliceFx.AotSample.Features.Health;

[Feature("GET /health", Summary = "Health check")]
public static class GetHealth
{
    public record Response(string Status, DateTimeOffset At);

    public static Task<Response> Handle(TimeProvider clock) =>
        Task.FromResult(new Response("ok", clock.GetUtcNow()));
}
