using System.Collections.Concurrent;

namespace Slice.Sample.Services;

/// <summary>
/// User entity stored by the sample application.
/// </summary>
/// <param name="Id">User identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Email">Email address.</param>
/// <param name="CreatedAt">UTC creation timestamp.</param>
public record User(Guid Id, string Name, string Email, DateTime CreatedAt);

/// <summary>
/// Minimal persistence abstraction used by the user feature examples.
/// </summary>
public interface IUserStore
{
    /// <summary>
    /// Adds a new user to the store.
    /// </summary>
    /// <param name="name">Display name to store.</param>
    /// <param name="email">Email address to store.</param>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns>The stored user, including generated fields.</returns>
    Task<User> AddAsync(string name, string email, CancellationToken ct);

    /// <summary>
    /// Finds a user by identifier.
    /// </summary>
    /// <param name="id">User identifier.</param>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns>The user when found; otherwise <see langword="null" />.</returns>
    Task<User?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Lists all stored users.
    /// </summary>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns>Users currently stored by the sample.</returns>
    Task<IReadOnlyList<User>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Removes a user from the store.
    /// </summary>
    /// <param name="id">User identifier.</param>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns><see langword="true" /> when a user was removed.</returns>
    Task<bool> RemoveAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// Thread-safe in-memory implementation for local sample runs.
/// </summary>
public sealed class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<Guid, User> _users = new();

    /// <inheritdoc />
    public Task<User> AddAsync(string name, string email, CancellationToken ct)
    {
        var user = new User(Guid.NewGuid(), name, email, DateTime.UtcNow);
        _users[user.Id] = user;
        return Task.FromResult(user);
    }

    /// <inheritdoc />
    public Task<User?> GetAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_users.TryGetValue(id, out var u) ? u : null);

    /// <inheritdoc />
    public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<User>>([.. _users.Values.OrderBy(u => u.CreatedAt)]);

    /// <inheritdoc />
    public Task<bool> RemoveAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_users.TryRemove(id, out _));
}
