namespace SliceFx.Wasi.Spin;

/// <summary>
/// Abstraction over Spin application variables (<c>fermyon:spin/variables@2.0.0</c>).
/// </summary>
/// <remarks>
/// On Fermyon Cloud / Spin, implement this interface using the WIT-generated free function.
/// componentize-dotnet emits it on a <c>*ImportsInterop</c> static class (e.g. <c>VariablesImportsInterop.Get</c>);
/// the generated <c>IVariablesImports</c> type carries only the <c>Error</c> shape. The underlying WIT
/// call is synchronous — wrap it with <c>ValueTask.FromResult(...)</c>, mirroring how
/// <c>IKeyValueStore</c> fronts the synchronous WASI key-value (<c>wasi:keyvalue</c>) host calls. Implementations are
/// fail-closed: undefined variable or provider error → <c>null</c>.
/// Use <see cref="InMemorySpinVariables"/> in unit tests.
/// </remarks>
public interface ISpinVariables
{
    /// <summary>
    /// Gets the value of the Spin variable named <paramref name="name"/>,
    /// or <c>null</c> if the variable is undefined or cannot be resolved.
    /// </summary>
    /// <param name="name">The variable name as declared in the <c>[variables]</c> section of <c>spin.toml</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<string?> GetAsync(string name, CancellationToken ct = default);
}
