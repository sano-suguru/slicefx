using System.ComponentModel.DataAnnotations;
using Slice.Workers;
using Slice.WorkersSample.Services;

namespace Slice.WorkersSample.Features.Echo;

[Feature("POST /echo", Summary = "Echo a message back")]
public static class PostEcho
{
    public record Request([Required, MinLength(1)] string Message);

    public record Response(string Echo, DateTimeOffset At);

    public static WorkerResponse Handle(Request req, IClock clock)
        => SliceResult.Ok(new Response(req.Message, clock.UtcNow));
}
