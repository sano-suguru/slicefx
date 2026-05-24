namespace Slice.SourceGenerator;

internal sealed record ValidatorModel(
    string ImplementationTypeFqn,
    string RequestTypeFqn,
    string ValidatorLocationFilePath,
    int ValidatorLocationSourceStart,
    int ValidatorLocationSourceLength,
    int ValidatorLocationStartLine,
    int ValidatorLocationStartCharacter,
    int ValidatorLocationEndLine,
    int ValidatorLocationEndCharacter)
{
    public DiagnosticLocationModel GetDiagnosticLocationModel()
        => new(
            ValidatorLocationFilePath,
            ValidatorLocationSourceStart,
            ValidatorLocationSourceLength,
            ValidatorLocationStartLine,
            ValidatorLocationStartCharacter,
            ValidatorLocationEndLine,
            ValidatorLocationEndCharacter);
}

internal readonly struct ValidatorResult(ValidatorModel? model, EquatableDiagnostic? diagnostic) : IEquatable<ValidatorResult>
{
    public ValidatorModel? Model { get; } = model;

    public EquatableDiagnostic? Diagnostic { get; } = diagnostic;

    public bool Equals(ValidatorResult other)
        => EqualityComparer<ValidatorModel?>.Default.Equals(Model, other.Model)
           && Nullable.Equals(Diagnostic, other.Diagnostic);

    public override bool Equals(object? obj) => obj is ValidatorResult other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((Model?.GetHashCode() ?? 0) * 397) ^ Diagnostic.GetHashCode();
        }
    }

    public static ValidatorResult Error(EquatableDiagnostic diagnostic) => new(null, diagnostic);
}
