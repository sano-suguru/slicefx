using Slice.LambdaSample.Features.Echo;

namespace Slice.LambdaSample.Validators;

/// <summary>
/// Demonstrates ISliceValidator — adds a rule that DataAnnotations cannot express.
/// Register in DI: services.AddScoped&lt;ISliceValidator&lt;PostEcho.Request&gt;, EchoRequestValidator&gt;()
/// </summary>
public sealed class EchoRequestValidator : ISliceValidator<PostEcho.Request>
{
    private static readonly string[] s_forbidden = ["FORBIDDEN", "BLOCKED"];

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
