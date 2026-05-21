using System.ComponentModel.DataAnnotations;

namespace Slice.WorkersSample.Features.Validation;

/// <summary>
/// Demonstrates generated validation behavior for supported DataAnnotations metadata.
/// </summary>
[Feature("POST /validation-fallback", Summary = "Exercise generated validation")]
public static class PostValidationFallback
{
    /// <summary>
    /// Request body used to probe generated DataAnnotations validation.
    /// </summary>
    /// <param name="Name">Required name value.</param>
    /// <param name="Items">Items that must satisfy minimum length validation.</param>
#pragma warning disable IL2026 // Slice generates Workers validation for int[] MinLength without runtime reflection.
    public record Request(
        [Required(ErrorMessage = "Name is required by custom validation.")] string? Name,
        [MinLength(2)] int[] Items);
#pragma warning restore IL2026

    /// <summary>
    /// Response body returned after generated validation succeeds.
    /// </summary>
    /// <param name="Name">Validated name value.</param>
    /// <param name="ItemCount">Number of validated items.</param>
    public record Response(string Name, int ItemCount);

    /// <summary>
    /// Returns validated request data for validation probes.
    /// </summary>
    /// <param name="req">Validated request body.</param>
    /// <returns>The validation fallback response payload.</returns>
    public static Response Handle(Request req)
        => new(req.Name!, req.Items.Length);
}
