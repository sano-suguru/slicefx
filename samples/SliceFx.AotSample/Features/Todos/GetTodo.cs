using SliceFx.AotSample.Services;

namespace SliceFx.AotSample.Features.Todos;

[Feature("GET /todos/{id:guid}", Summary = "Get a todo by id")]
public static class GetTodo
{
    public static async Task<SliceResult<Todo>> Handle(Guid id, ITodoStore store, CancellationToken ct)
    {
        var todo = await store.GetAsync(id, ct);
        return todo is null
            ? SliceResult<Todo>.NotFound($"Todo '{id}' not found.")
            : SliceResult<Todo>.Ok(todo);
    }
}
