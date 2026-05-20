using Slice.Sample.Services;

namespace Slice.Sample.Features.Users;

[Feature("GET /users", Summary = "List all users")]
public static class ListUsers
{
    public record Item(Guid Id, string Name, string Email);

    public record Response(int Count, IReadOnlyList<Item> Items);

    public static async Task<Response> Handle(IUserStore store, CancellationToken ct)
    {
        var users = await store.ListAsync(ct).ConfigureAwait(false);
        var items = users.Select(u => new Item(u.Id, u.Name, u.Email)).ToList();
        return new Response(items.Count, items);
    }
}
