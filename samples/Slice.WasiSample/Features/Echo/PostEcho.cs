using System.ComponentModel.DataAnnotations;

namespace Slice.WasiSample.Features.Echo;

/// <summary>
/// Echoes a WASI request body and stamps it with the injected clock.
/// </summary>
[Feature("POST /echo", Summary = "Echo a message back")]
public static class PostEcho
{
    /// <summary>
    /// Request body for the WASI echo endpoint.
    /// </summary>
    /// <param name="Message">Message to echo back.</param>
    public record Request([Required, StringLength(int.MaxValue, MinimumLength = 1)] string Message);

    /// <summary>
    /// Response body for the WASI echo endpoint.
    /// </summary>
    /// <param name="Echo">Original request message.</param>
    /// <param name="At">UTC time provided by the sample TimeProvider service.</param>
    public record Response(string Echo, DateTimeOffset At);

    /// <summary>
    /// Produces an echo response without ASP.NET-specific result types.
    /// </summary>
    /// <param name="req">Validated request body.</param>
    /// <param name="timeProvider">Clock service resolved from the wasi:http host.</param>
    /// <returns>The echo response payload.</returns>
    public static Response Handle(Request req, TimeProvider timeProvider)
        => new(req.Message, timeProvider.GetUtcNow());
}
