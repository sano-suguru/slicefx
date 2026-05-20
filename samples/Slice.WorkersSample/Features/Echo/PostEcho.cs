using System.ComponentModel.DataAnnotations;
using Slice.WorkersSample.Services;

namespace Slice.WorkersSample.Features.Echo;

[Feature("POST /echo", Summary = "Echo a message back")]
public static class PostEcho
{
    public record Request([Required, StringLength(int.MaxValue, MinimumLength = 1)] string Message);

    public record Response(string Echo, DateTimeOffset At);

    public static Response Handle(Request req, IClock clock)
        => new(req.Message, clock.UtcNow);
}
