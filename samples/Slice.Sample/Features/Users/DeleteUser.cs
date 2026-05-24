using Slice.Sample.Filters;
using Slice.Sample.Services;

namespace Slice.Sample.Features.Users;

/// <summary>
/// Deletes a user after request logging and API-key filters run.
/// </summary>
[Feature("DELETE /users/{id:guid}", Summary = "Delete a user (demo API key filter)")]
[Filter<RequestLoggingFilter>]
[Filter<RequireApiKeyFilter>]
public static class DeleteUser
{
    /// <summary>
    /// Removes the requested user when it exists.
    /// </summary>
    /// <param name="id">User identifier from the route.</param>
    /// <param name="store">User store resolved from dependency injection.</param>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns><c>204 No Content</c> when deleted, or <c>404 Not Found</c>.</returns>
    public static async Task<IResult> Handle(Guid id, IUserStore store, CancellationToken ct)
    {
        var user = await store.GetAsync(id, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Results.NotFound();
        }

        await store.RemoveAsync(id, ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}
