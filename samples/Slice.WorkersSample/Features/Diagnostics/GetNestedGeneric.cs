namespace Slice.WorkersSample.Features.Diagnostics;

[Feature("GET /nested-generic", Summary = "Return a nested generic response")]
public static class GetNestedGeneric
{
    public static Task<Dictionary<string, List<int>>> Handle()
    {
        var response = new Dictionary<string, List<int>>(StringComparer.Ordinal)
        {
            ["values"] = [1, 2, 3],
        };

        return Task.FromResult(response);
    }
}
