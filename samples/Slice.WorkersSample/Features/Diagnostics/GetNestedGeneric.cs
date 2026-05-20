namespace Slice.WorkersSample.Features.Diagnostics;

/// <summary>
/// Demonstrates that Workers can serialize nested generic response types.
/// </summary>
[Feature("GET /nested-generic", Summary = "Return a nested generic response")]
public static class GetNestedGeneric
{
    /// <summary>
    /// Returns a dictionary payload used by the probe diagnostics.
    /// </summary>
    /// <returns>A nested generic response containing sample values.</returns>
    public static Task<Dictionary<string, List<int>>> Handle()
    {
        var response = new Dictionary<string, List<int>>(StringComparer.Ordinal)
        {
            ["values"] = [1, 2, 3],
        };

        return Task.FromResult(response);
    }
}
