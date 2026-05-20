using System.Collections.Concurrent;

namespace Slice.Sample.Services;

public record User(Guid Id, string Name, string Email, DateTime CreatedAt);

public interface IUserStore
{
    Task<User> AddAsync(string name, string email, CancellationToken ct);
    Task<User?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<User>> ListAsync(CancellationToken ct);
    Task<bool> RemoveAsync(Guid id, CancellationToken ct);
}

public sealed class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<Guid, User> _users = new();

    public Task<User> AddAsync(string name, string email, CancellationToken ct)
    {
        var user = new User(Guid.NewGuid(), name, email, DateTime.UtcNow);
        _users[user.Id] = user;
        return Task.FromResult(user);
    }

    public Task<User?> GetAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_users.TryGetValue(id, out var u) ? u : null);

    public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<User>>([.. _users.Values.OrderBy(u => u.CreatedAt)]);

    public Task<bool> RemoveAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_users.TryRemove(id, out _));
}
