using System.ComponentModel.DataAnnotations;
using SliceFx.AotSample.Services;

namespace SliceFx.AotSample.Features.Todos;

[Feature("POST /todos", Summary = "Create a todo")]
public static class CreateTodo
{
    public record Request(
        [Required, StringLength(200, MinimumLength = 1)] string Title);

    public static async Task<Todo> Handle(Request req, ITodoStore store, CancellationToken ct) =>
        await store.CreateAsync(req.Title, ct);
}
