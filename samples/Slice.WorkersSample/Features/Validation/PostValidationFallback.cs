using System.ComponentModel.DataAnnotations;

namespace Slice.WorkersSample.Features.Validation;

/// <summary>
/// Demonstrates validation behavior when a Workers route uses fallback validation.
/// </summary>
[Feature("POST /validation-fallback", Summary = "Exercise reflection validation fallback")]
public static class PostValidationFallback
{
    /// <summary>
    /// Request body used to probe fallback DataAnnotations validation.
    /// </summary>
    /// <param name="Name">Required name value.</param>
    /// <param name="Items">Items that must satisfy minimum length validation.</param>
    public record Request(
        [Required(ErrorMessage = "Name is required by custom validation.")] string? Name,
        [MinLength(2)] int[] Items);

    /// <summary>
    /// Response body returned after fallback validation succeeds.
    /// </summary>
    /// <param name="Name">Validated name value.</param>
    /// <param name="ItemCount">Number of validated items.</param>
    public record Response(string Name, int ItemCount);

    /// <summary>
    /// Returns validated request data for fallback-validation probes.
    /// </summary>
    /// <param name="req">Validated request body.</param>
    /// <returns>The validation fallback response payload.</returns>
    public static Response Handle(Request req)
        => new(req.Name!, req.Items.Length);
}
