using System.ComponentModel.DataAnnotations;

namespace Slice.LambdaSample.Features.Echo;

/// <summary>
/// Demonstrates a Lambda-ready endpoint with DataAnnotations and custom Slice validation.
/// </summary>
[Feature("POST /echo", Summary = "Echo the request body back")]
[Filter<SliceValidatorFilter<Request>>]   // custom imperative validation via ISliceValidator<Request>
public static class PostEcho
{
    /// <summary>
    /// Request body accepted by the echo endpoint.
    /// </summary>
    /// <param name="Message">Message to echo back.</param>
    public record Request([Required, MinLength(1)] string Message);

    /// <summary>
    /// Echo response returned to the caller.
    /// </summary>
    /// <param name="Echo">Original request message.</param>
    /// <param name="Timestamp">UTC time when the response was created.</param>
    public record Response(string Echo, DateTime Timestamp);

    /// <summary>
    /// Returns the request message with a timestamp.
    /// </summary>
    /// <param name="req">Validated request body.</param>
    /// <param name="timeProvider">Clock service resolved from the Lambda sample host.</param>
    /// <returns>An HTTP 200 response containing the echo payload.</returns>
    public static IResult Handle(Request req, TimeProvider timeProvider)
        => Results.Ok(new Response(req.Message, timeProvider.GetUtcNow().UtcDateTime));
}
