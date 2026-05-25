using SliceFx.Sample.Services;

namespace SliceFx.Sample.Features.Users;

/// <summary>
/// Lists users currently held by the sample in-memory store.
/// </summary>
[Feature("GET /users", Summary = "List all users")]
public static class ListUsers
{
    /// <summary>
    /// Compact user item returned from the list endpoint.
    /// </summary>
    /// <param name="Id">User identifier.</param>
    /// <param name="Name">Stored display name.</param>
    /// <param name="Email">Stored email address.</param>
    public record Item(Guid Id, string Name, string Email);

    /// <summary>
    /// Response body for the users list endpoint.
    /// </summary>
    /// <param name="Count">Number of returned users.</param>
    /// <param name="Items">Users ordered by creation time.</param>
    public record Response(int Count, IReadOnlyList<Item> Items);

    /// <summary>
    /// Reads all users and projects them to list items.
    /// </summary>
    /// <param name="store">User store resolved from dependency injection.</param>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns>The user list response.</returns>
    public static async Task<Response> Handle(IUserStore store, CancellationToken ct)
    {
        var users = await store.ListAsync(ct).ConfigureAwait(false);
        var items = users.Select(u => new Item(u.Id, u.Name, u.Email)).ToList();
        return new Response(items.Count, items);
    }
}
