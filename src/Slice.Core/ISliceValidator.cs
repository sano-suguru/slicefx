namespace Slice;

/// <summary>
/// Validates a value of type <typeparamref name="T"/>.
/// Implement this interface for request-level validation that goes beyond DataAnnotations
/// (cross-field rules, database lookups, etc.).
/// </summary>
/// <remarks>
/// Registration pattern:
/// <list type="number">
///   <item>Declare <c>[Filter&lt;SliceValidatorFilter&lt;Request&gt;&gt;]</c> on the feature class.</item>
///   <item>Register the implementation: <c>services.AddScoped&lt;ISliceValidator&lt;Request&gt;, MyValidator&gt;()</c>.</item>
/// </list>
/// Validator implementations are registered manually because they are application services, not
/// Slice features. This keeps discovery explicit, avoids runtime assembly scanning, and lets the
/// application choose lifetimes and environment-specific replacements.
/// <see cref="DataAnnotationsValidationFilter"/> is always attached before <c>[Filter&lt;T&gt;]</c>
/// filters. <c>SliceValidatorFilter&lt;T&gt;</c> then participates in normal declaration order with
/// other filters. Returning <see cref="SliceValidationResult.Success"/> passes control to the
/// next filter; returning <c>SliceValidationResult.Failure(...)</c> short-circuits with a
/// 400 Problem Details response.
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
