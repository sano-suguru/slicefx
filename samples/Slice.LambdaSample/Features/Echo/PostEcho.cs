using System.ComponentModel.DataAnnotations;

namespace Slice.LambdaSample.Features.Echo;

[Feature("POST /echo", Summary = "Echo the request body back")]
[Filter<SliceValidatorFilter<Request>>]   // custom imperative validation via ISliceValidator<Request>
public static class PostEcho
{
    public record Request([Required, MinLength(1)] string Message);

    public record Response(string Echo, DateTime Timestamp);

    public static IResult Handle(Request req)
        => Results.Ok(new Response(req.Message, DateTime.UtcNow));
}
