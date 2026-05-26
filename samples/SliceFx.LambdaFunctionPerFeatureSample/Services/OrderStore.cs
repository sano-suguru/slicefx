namespace SliceFx.LambdaFunctionPerFeatureSample.Services;

/// <summary>
/// Provides order read and write operations.
/// </summary>
public interface IOrderStore
{
    /// <summary>Returns an order by ID.</summary>
    /// <param name="id">Order identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<Order> GetAsync(int id, CancellationToken ct);

    /// <summary>Creates and returns a new order.</summary>
    /// <param name="sku">Stock-keeping unit identifier.</param>
    /// <param name="quantity">Number of units.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<Order> AddAsync(string sku, int quantity, CancellationToken ct);
}

/// <summary>
/// In-memory order store for local development and Lambda demo purposes.
/// </summary>
public sealed class InMemoryOrderStore : IOrderStore
{
    /// <inheritdoc />
    public ValueTask<Order> GetAsync(int id, CancellationToken ct)
        => ValueTask.FromResult(new Order(id, "sample-sku", 1));

    /// <inheritdoc />
    public ValueTask<Order> AddAsync(string sku, int quantity, CancellationToken ct)
        => ValueTask.FromResult(new Order(42, sku, quantity));
}

/// <summary>
/// Represents a stored order.
/// </summary>
/// <param name="Id">Assigned order identifier.</param>
/// <param name="Sku">Stock-keeping unit identifier.</param>
/// <param name="Quantity">Number of units ordered.</param>
public sealed record Order(int Id, string Sku, int Quantity);
