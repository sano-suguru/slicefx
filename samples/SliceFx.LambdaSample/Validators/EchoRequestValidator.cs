using SliceFx.LambdaSample.Features.Echo;

namespace SliceFx.LambdaSample.Validators;

/// <summary>
/// Demonstrates ISliceValidator — adds a rule that DataAnnotations cannot express.
/// </summary>
public sealed class EchoRequestValidator : ISliceValidator<PostEcho.Request>
{
    private static readonly string[] s_forbidden = ["FORBIDDEN", "BLOCKED"];

    /// <summary>
    /// Rejects echo messages containing demo-only forbidden words.
    /// </summary>
    /// <param name="value">Request value to validate.</param>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns>A Slice validation result for the request.</returns>
    public ValueTask<SliceValidationResult> ValidateAsync(PostEcho.Request value, CancellationToken ct)
    {
        foreach (var word in s_forbidden)
        {
            if (value.Message.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(SliceValidationResult.Failure(
                    nameof(value.Message), $"Message contains disallowed content: '{word}'."));
            }
        }

        return ValueTask.FromResult(SliceValidationResult.Success);
    }
}
