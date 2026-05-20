using Slice.Sample.Filters;
using Slice.Sample.Services;

namespace Slice.Sample.Features.Users;

[Feature("DELETE /users/{id:guid}", Summary = "Delete a user (requires API key)")]
[Filter<RequestLoggingFilter>]
[Filter<RequireApiKeyFilter>]
public static class DeleteUser
{
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
