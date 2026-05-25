namespace SliceFx;

/// <summary>
/// The outcome of an <see cref="ISliceValidator{T}"/> validation run.
/// Use <see cref="Success"/> for a passing result, or one of the <c>Failure(...)</c> overloads
/// to return field-keyed error messages that are forwarded as Problem Details.
/// </summary>
public sealed class SliceValidationResult
{
    /// <summary>A cached instance representing a successful (no-error) result.</summary>
    public static SliceValidationResult Success { get; } = new(isValid: true, errors: null);

    private SliceValidationResult(bool isValid, IReadOnlyDictionary<string, string[]>? errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    /// <summary><c>true</c> when validation passed; <c>false</c> when it failed.</summary>
    public bool IsValid { get; }

    /// <summary>
    /// Field-keyed error messages. <c>null</c> when <see cref="IsValid"/> is <c>true</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    /// <summary>Returns a failure result with the given pre-built error dictionary.</summary>
    /// <param name="errors">A non-empty dictionary of field names to validation messages.</param>
    /// <returns>A failed validation result.</returns>
    public static SliceValidationResult Failure(IReadOnlyDictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return new SliceValidationResult(false, CopyAndValidateErrors(errors));
    }

    /// <summary>Returns a failure result for a single field with one or more error messages.</summary>
    /// <param name="field">The field name associated with the validation errors.</param>
    /// <param name="messages">One or more validation messages for the field.</param>
    /// <returns>A failed validation result.</returns>
    public static SliceValidationResult Failure(string field, params string[] messages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(messages);
        ValidateMessages(field, messages, nameof(messages));

        return new SliceValidationResult(
            false,
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [.. messages] });
    }

    private static Dictionary<string, string[]> CopyAndValidateErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        if (errors.Count == 0)
        {
            throw new ArgumentException("At least one validation error is required.", nameof(errors));
        }

        var copy = new Dictionary<string, string[]>(errors.Count, StringComparer.Ordinal);
        foreach (var (field, messages) in errors)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field, nameof(errors));
            ValidateMessages(field, messages, nameof(errors));
            copy.Add(field, [.. messages]);
        }

        return copy;
    }

    private static void ValidateMessages(string field, string[] messages, string paramName)
    {
        ArgumentNullException.ThrowIfNull(messages, paramName);
        if (messages.Length == 0)
        {
            throw new ArgumentException($"At least one validation message is required for field '{field}'.", paramName);
        }

        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException($"Validation messages for field '{field}' cannot be null, empty, or whitespace.", paramName);
            }
        }
    }
}
