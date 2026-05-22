using System.Collections.Immutable;

namespace Slice.SourceGenerator;

internal sealed record LambdaPerFunctionOptions(
    bool Enabled,
    string? StartupTypeFqn,
    ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> Diagnostics);
