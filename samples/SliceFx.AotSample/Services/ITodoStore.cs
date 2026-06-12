using SliceFx.AotSample.Features.Todos;

namespace SliceFx.AotSample.Services;

public interface ITodoStore
{
    Task<Todo> CreateAsync(string title, CancellationToken ct = default);
    Task<Todo?> GetAsync(Guid id, CancellationToken ct = default);
    Task<List<Todo>> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
