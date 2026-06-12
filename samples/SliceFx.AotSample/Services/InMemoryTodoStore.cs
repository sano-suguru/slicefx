using System.Collections.Concurrent;
using SliceFx.AotSample.Features.Todos;

namespace SliceFx.AotSample.Services;

public sealed class InMemoryTodoStore : ITodoStore
{
    private readonly ConcurrentDictionary<Guid, Todo> _store = new();

    public Task<Todo> CreateAsync(string title, CancellationToken ct = default)
    {
        var todo = new Todo(Guid.NewGuid(), title, DateTime.UtcNow);
        _store[todo.Id] = todo;
        return Task.FromResult(todo);
    }

    public Task<Todo?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(id, out var todo) ? todo : null);

    public Task<List<Todo>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<List<Todo>>([.. _store.Values]);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
