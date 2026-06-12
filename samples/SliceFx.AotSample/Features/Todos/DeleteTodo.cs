using SliceFx.AotSample.Services;

namespace SliceFx.AotSample.Features.Todos;

[Feature("DELETE /todos/{id:guid}", Summary = "Delete a todo")]
public static class DeleteTodo
{
    public static async Task Handle(Guid id, ITodoStore store, CancellationToken ct) =>
        await store.DeleteAsync(id, ct);
}
