using SliceFx.Sample.Filters;
using SliceFx.Sample.Services;

namespace SliceFx.Sample.Features.Users;

/// <summary>
/// Returns the identity of the authenticated caller.
/// Demonstrates the <see cref="UserAuthFilter"/> + scoped <see cref="CurrentUser"/> pattern:
/// the filter resolves the identity and stores it; this handler just injects it.
/// </summary>
[Feature("GET /users/me", Summary = "Get the authenticated user")]
[SliceFilter<UserAuthFilter>]
public static class GetCurrentUser
{
    /// <summary>Response payload for the authenticated user endpoint.</summary>
    /// <param name="Name">Display name of the authenticated user.</param>
    public record Response(string Name);

    /// <summary>Returns the authenticated user resolved by <see cref="UserAuthFilter"/>.</summary>
    public static SliceResult<Response> Handle(CurrentUser user)
        => SliceResult<Response>.Ok(new Response(user.Name));
}
