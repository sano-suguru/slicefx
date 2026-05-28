using System.ComponentModel.DataAnnotations;

namespace SliceFx.BlazorSample.Contracts;

/// <summary>
/// Request body for creating a new user.
/// </summary>
/// <remarks>
/// Uses explicit <c>{ get; set; }</c> properties rather than positional record syntax so that
/// Blazor's <c>EditForm</c> can bind to these fields via two-way <c>@bind-Value</c>
/// (which requires a writable setter, not <c>init</c>).
/// </remarks>
public record CreateUserRequest
{
    /// <summary>Display name for the new user (at least 2 characters).</summary>
    [Required, MinLength(2)]
    public string Name { get; set; } = "";

    /// <summary>Email address for the new user.</summary>
    [Required, EmailAddress]
    public string Email { get; set; } = "";
}

/// <summary>
/// Response body returned after a user is created.
/// </summary>
/// <param name="Id">Generated user identifier.</param>
/// <param name="Name">Stored display name.</param>
/// <param name="Email">Stored email address.</param>
/// <param name="CreatedAt">UTC creation timestamp.</param>
public record CreateUserResponse(Guid Id, string Name, string Email, DateTime CreatedAt);

/// <summary>
/// Compact user projection returned by the list endpoint.
/// </summary>
/// <param name="Id">User identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Email">Email address.</param>
public record UserSummary(Guid Id, string Name, string Email);
