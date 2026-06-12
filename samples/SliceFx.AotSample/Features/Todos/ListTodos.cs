using SliceFx.AotSample.Services;

namespace SliceFx.AotSample.Features.Todos;

[Feature("GET /todos", Summary = "List all todos")]
public static class ListTodos
{
    public static async Task<List<Todo>> Handle(ITodoStore store, CancellationToken ct) =>
        await store.ListAsync(ct);
}
