using System.ComponentModel.DataAnnotations;
using SliceFx.Sample.Services;

namespace SliceFx.Sample.Features.Users;

/// <summary>
/// Creates a user from a validated JSON request body.
/// </summary>
[Feature("POST /users", Summary = "Create a new user")]
public static class CreateUser
{
    /// <summary>
    /// Request body for creating a user.
    /// </summary>
    /// <param name="Name">Display name for the new user.</param>
    /// <param name="Email">Email address for the new user.</param>
    public record Request(
        [Required, MinLength(2)] string Name,
        [Required, EmailAddress] string Email);

    /// <summary>
    /// Response body returned after a user is created.
    /// </summary>
    /// <param name="Id">Generated user identifier.</param>
    /// <param name="Name">Stored display name.</param>
    /// <param name="Email">Stored email address.</param>
    /// <param name="CreatedAt">UTC creation timestamp.</param>
    public record Response(Guid Id, string Name, string Email, DateTime CreatedAt);

    /// <summary>
    /// Persists the user and returns the created user payload.
    /// </summary>
    /// <param name="req">Validated request body.</param>
    /// <param name="store">User store resolved from dependency injection.</param>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns>The created user payload.</returns>
    public static async Task<Response> Handle(Request req, IUserStore store, CancellationToken ct)
    {
        var user = await store.AddAsync(req.Name, req.Email, ct).ConfigureAwait(false);
        return new Response(user.Id, user.Name, user.Email, user.CreatedAt);
    }
}
