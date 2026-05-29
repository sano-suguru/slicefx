namespace SliceFx.Wasi.Spin;

/// <summary>
/// In-memory <see cref="ISpinVariables"/> implementation for unit tests and local development.
/// </summary>
public sealed class InMemorySpinVariables : ISpinVariables
{
    private readonly Dictionary<string, string> _variables;

    /// <summary>Initializes a new instance with an empty variable set.</summary>
    public InMemorySpinVariables() => _variables = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance pre-seeded with <paramref name="variables"/>.</summary>
    public InMemorySpinVariables(IEnumerable<KeyValuePair<string, string>> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        _variables = new(variables, StringComparer.Ordinal);
    }


    /// <inheritdoc/>
    public ValueTask<string?> GetAsync(string name, CancellationToken ct = default)
    {
        _variables.TryGetValue(name, out var value);
        return ValueTask.FromResult(value);
    }

    /// <summary>Sets <paramref name="name"/> to <paramref name="value"/>. Overwrites any existing entry.</summary>
    public void Set(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _variables[name] = value;
    }

    /// <summary>Removes all variables. Useful for resetting state between tests.</summary>
    public void Clear() => _variables.Clear();
}
