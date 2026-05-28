using SliceFx.BlazorSample.Contracts;
using SliceFx.BlazorSample.Server.Services;

namespace SliceFx.BlazorSample.Server.Features.Users;

/// <summary>
/// Creates a user from a validated JSON request body.
/// </summary>
/// <remarks>
/// Demonstrates the shared-contracts pattern: <see cref="CreateUserRequest" /> and
/// <see cref="CreateUserResponse" /> are non-nested types from the
/// <c>SliceFx.BlazorSample.Contracts</c> project, referenced by both this server feature
/// and the Blazor WASM client directly.
/// </remarks>
[Feature("POST /users", Summary = "Create a new user")]
public static class CreateUser
{
    /// <summary>
    /// Persists the user and returns the created user payload.
    /// </summary>
    /// <param name="req">Validated request body.</param>
    /// <param name="store">User store resolved from dependency injection.</param>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns>The created user payload.</returns>
    public static async Task<CreateUserResponse> Handle(
        CreateUserRequest req, IUserStore store, CancellationToken ct)
    {
        var user = await store.AddAsync(req.Name, req.Email, ct).ConfigureAwait(false);
        return new CreateUserResponse(user.Id, user.Name, user.Email, user.CreatedAt);
    }
}
