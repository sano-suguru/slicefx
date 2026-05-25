using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace SliceFx.WasiSample.Features.Validation;

/// <summary>
/// Exercises generated validation for array <see cref="MinLengthAttribute" /> metadata.
/// </summary>
[Feature("POST /array-min-length", Summary = "Validate array MinLength without reflection fallback")]
public static class PostArrayMinLength
{
    /// <summary>
    /// Request body containing an array that must have at least two items.
    /// </summary>
    /// <param name="Items">Items to count after validation.</param>
    [method: UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Slice generates WASI validation for int[] MinLength without runtime reflection.")]
    public record Request([MinLength(2)] int[] Items);

    /// <summary>
    /// Response body returned after array validation succeeds.
    /// </summary>
    /// <param name="ItemCount">Number of items in the request.</param>
    public record Response(int ItemCount);

    /// <summary>
    /// Returns the length of the validated array.
    /// </summary>
    /// <param name="req">Validated request body.</param>
    /// <returns>The item-count response payload.</returns>
    public static Response Handle(Request req)
        => new(req.Items.Length);
}
