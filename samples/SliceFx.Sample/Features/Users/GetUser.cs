using SliceFx.Sample.Services;

namespace SliceFx.Sample.Features.Users;

/// <summary>
/// Looks up a single user by route id.
/// </summary>
[Feature("GET /users/{id:guid}", Summary = "Get a user by id")]
public static class GetUser
{
    /// <summary>
    /// User details returned by the lookup endpoint.
    /// </summary>
    /// <param name="Id">User identifier.</param>
    /// <param name="Name">Stored display name.</param>
    /// <param name="Email">Stored email address.</param>
    /// <param name="CreatedAt">UTC creation timestamp.</param>
    public record Response(Guid Id, string Name, string Email, DateTime CreatedAt);

    /// <summary>
    /// Returns the matching user or <c>404 Not Found</c>.
    /// </summary>
    /// <param name="id">User identifier from the route.</param>
    /// <param name="store">User store resolved from dependency injection.</param>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns>An HTTP result containing the user when found.</returns>
    public static async Task<IResult> Handle(Guid id, IUserStore store, CancellationToken ct)
    {
        var user = await store.GetAsync(id, ct).ConfigureAwait(false);
        return user is null
            ? Results.NotFound()
            : Results.Ok(new Response(user.Id, user.Name, user.Email, user.CreatedAt));
    }
}
