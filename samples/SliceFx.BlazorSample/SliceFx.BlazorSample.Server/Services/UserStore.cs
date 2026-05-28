using System.Collections.Concurrent;

namespace SliceFx.BlazorSample.Server.Services;

/// <summary>
/// User entity stored by the Blazor sample server.
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
    /// Lists all stored users.
    /// </summary>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns>Users currently stored by the sample, ordered by creation time.</returns>
    Task<IReadOnlyList<User>> ListAsync(CancellationToken ct);
}

/// <summary>
/// Thread-safe in-memory implementation for local sample runs.
/// </summary>
public sealed class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<Guid, User> _users = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new in-memory user store with the clock used for generated timestamps.
    /// </summary>
    /// <param name="timeProvider">Clock service resolved from the sample host.</param>
    public InMemoryUserStore(TimeProvider timeProvider)
        => _timeProvider = timeProvider;

    /// <inheritdoc />
    public Task<User> AddAsync(string name, string email, CancellationToken ct)
    {
        var user = new User(Guid.NewGuid(), name, email, _timeProvider.GetUtcNow().UtcDateTime);
        _users[user.Id] = user;
        return Task.FromResult(user);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<User>>([.. _users.Values.OrderBy(u => u.CreatedAt)]);
}
