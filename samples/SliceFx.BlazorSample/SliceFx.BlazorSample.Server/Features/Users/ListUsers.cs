using SliceFx.BlazorSample.Contracts;
using SliceFx.BlazorSample.Server.Services;

namespace SliceFx.BlazorSample.Server.Features.Users;

/// <summary>
/// Returns all stored users as a compact summary list.
/// </summary>
[Feature("GET /users", Summary = "List all users")]
public static class ListUsers
{
    /// <summary>
    /// Returns all users currently held in the store.
    /// </summary>
    /// <param name="store">User store resolved from dependency injection.</param>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns>A read-only list of user summaries ordered by creation time.</returns>
    public static async Task<IReadOnlyList<UserSummary>> Handle(IUserStore store, CancellationToken ct)
    {
        var users = await store.ListAsync(ct).ConfigureAwait(false);
        return [.. users.Select(u => new UserSummary(u.Id, u.Name, u.Email))];
    }
}
