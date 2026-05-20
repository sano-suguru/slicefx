using Slice.Sample.Services;

namespace Slice.Sample.Features.Users;

[Feature("GET /users/{id:guid}", Summary = "Get a user by id")]
public static class GetUser
{
    public record Response(Guid Id, string Name, string Email, DateTime CreatedAt);

    public static async Task<IResult> Handle(Guid id, IUserStore store, CancellationToken ct)
    {
        var user = await store.GetAsync(id, ct).ConfigureAwait(false);
        return user is null
            ? Results.NotFound()
            : Results.Ok(new Response(user.Id, user.Name, user.Email, user.CreatedAt));
    }
}
