using System.ComponentModel.DataAnnotations;

namespace Slice.WorkersSample.Features.Validation;

[Feature("POST /array-min-length", Summary = "Validate array MinLength without reflection fallback")]
public static class PostArrayMinLength
{
    public record Request([MinLength(2)] int[] Items);

    public record Response(int ItemCount);

    public static Response Handle(Request req)
        => new(req.Items.Length);
}
