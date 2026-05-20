using System.ComponentModel.DataAnnotations;
using Slice.Sample.Services;

namespace Slice.Sample.Features.Users;

[Feature("POST /users", Summary = "Create a new user")]
public static class CreateUser
{
    public record Request(
        [Required, MinLength(2)] string Name,
        [Required, EmailAddress] string Email);

    public record Response(Guid Id, string Name, string Email, DateTime CreatedAt);

    public static async Task<IResult> Handle(Request req, IUserStore store, CancellationToken ct)
    {
        var user = await store.AddAsync(req.Name, req.Email, ct).ConfigureAwait(false);
        return Results.Created($"/users/{user.Id}",
            new Response(user.Id, user.Name, user.Email, user.CreatedAt));
    }
}
