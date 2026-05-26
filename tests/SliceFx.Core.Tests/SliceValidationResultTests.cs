namespace SliceFx.Core.Tests;

public sealed class SliceValidationResultTests
{
    [Fact]
    public void Success_is_valid_singleton_without_errors()
    {
        Assert.Same(SliceValidationResult.Success, SliceValidationResult.Success);
        Assert.True(SliceValidationResult.Success.IsValid);
        Assert.Null(SliceValidationResult.Success.Errors);
    }

    [Fact]
    public void Failure_copies_error_dictionary_and_message_arrays()
    {
        var messages = new[] { "Original message." };
        var errors = new Dictionary<string, string[]>
        {
            ["Name"] = messages,
        };

        var result = SliceValidationResult.Failure(errors);
        messages[0] = "Mutated message.";
        errors["Name"] = ["Replaced message."];

        Assert.False(result.IsValid);
        Assert.NotNull(result.Errors);
        var resultMessages = result.Errors["Name"];
        Assert.Equal(["Original message."], resultMessages);
    }

    [Fact]
    public void Failure_rejects_invalid_error_dictionary()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SliceValidationResult.Failure((IReadOnlyDictionary<string, string[]>)null!));
        Assert.Throws<ArgumentException>(() =>
            SliceValidationResult.Failure(new Dictionary<string, string[]>()));
        Assert.Throws<ArgumentException>(() =>
            SliceValidationResult.Failure(new Dictionary<string, string[]> { [" "] = ["Required."] }));
        Assert.Throws<ArgumentException>(() =>
            SliceValidationResult.Failure(new Dictionary<string, string[]> { ["Name"] = [] }));
        Assert.Throws<ArgumentException>(() =>
            SliceValidationResult.Failure(new Dictionary<string, string[]> { ["Name"] = [" "] }));
    }

    [Fact]
    public void Failure_rejects_invalid_single_field_arguments()
    {
        Assert.Throws<ArgumentException>(() => SliceValidationResult.Failure(" ", "Required."));
        Assert.Throws<ArgumentNullException>(() => SliceValidationResult.Failure("Name", null!));
        Assert.Throws<ArgumentException>(() => SliceValidationResult.Failure("Name"));
        Assert.Throws<ArgumentException>(() => SliceValidationResult.Failure("Name", " "));
    }
}
