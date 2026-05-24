namespace Slice;

/// <summary>
/// Validates a value of type <typeparamref name="T"/>.
/// Implement this interface for request-level validation that goes beyond DataAnnotations
/// (cross-field rules, database lookups, etc.).
/// </summary>
/// <remarks>
/// Implement a closed <c>ISliceValidator&lt;Request&gt;</c> type to attach request-level
/// validation to a Slice feature. The source generator registers validators as scoped services
/// and runs them automatically for matching request parameters.
/// <see cref="DataAnnotationsValidationFilter"/> runs first, then <c>ISliceValidator&lt;T&gt;</c>,
/// then user-declared <c>[Filter&lt;T&gt;]</c> filters. Returning
/// <see cref="SliceValidationResult.Success"/> passes control to the next step; returning
/// <c>SliceValidationResult.Failure(...)</c> short-circuits with a 400 Problem Details response.
/// </remarks>
/// <typeparam name="T">The value type to validate.</typeparam>
public interface ISliceValidator<T>
{
    /// <summary>
    /// Validates the supplied value.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="ct">A token that is canceled when the request is aborted.</param>
    /// <returns>The validation result.</returns>
    ValueTask<SliceValidationResult> ValidateAsync(T value, CancellationToken ct);
}
