using System.ComponentModel.DataAnnotations;

namespace Slice.WorkersSample.Features.Validation;

[Feature("POST /validation-fallback", Summary = "Exercise reflection validation fallback")]
public static class PostValidationFallback
{
    public record Request(
        [Required(ErrorMessage = "Name is required by custom validation.")] string? Name,
        [MinLength(2)] int[] Items);

    public record Response(string Name, int ItemCount);

    public static Response Handle(Request req)
        => new(req.Name!, req.Items.Length);
}
