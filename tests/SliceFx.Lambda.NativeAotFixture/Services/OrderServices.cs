namespace SliceFx.Lambda.NativeAotFixture.Services;

public interface IOrderStore
{
    ValueTask<OrderDto> GetAsync(int id, CancellationToken ct);

    ValueTask<OrderDto> AddAsync(string sku, int quantity, CancellationToken ct);
}

public sealed class InMemoryOrderStore : IOrderStore
{
    public ValueTask<OrderDto> GetAsync(int id, CancellationToken ct)
        => ValueTask.FromResult(new OrderDto(id, "fixture-sku", 1));

    public ValueTask<OrderDto> AddAsync(string sku, int quantity, CancellationToken ct)
        => ValueTask.FromResult(new OrderDto(42, sku, quantity));
}

public sealed record OrderDto(int Id, string Sku, int Quantity);
