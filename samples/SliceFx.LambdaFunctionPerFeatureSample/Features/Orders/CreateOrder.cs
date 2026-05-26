using System.ComponentModel.DataAnnotations;
using SliceFx.Lambda.FunctionPerFeature;
using SliceFx.LambdaFunctionPerFeatureSample.Services;

namespace SliceFx.LambdaFunctionPerFeatureSample.Features.Orders;

/// <summary>
/// Creates a new order. Demonstrates body binding, DataAnnotations validation, and
/// <see cref="ISliceValidator{T}"/> custom validation in the function-per-feature Lambda path.
/// </summary>
[Feature("POST /orders", Summary = "Create an order")]
[LambdaFunctionStartup(typeof(OrderFeatureStartup))]
public static class CreateOrder
{
    /// <summary>
    /// Order creation request body.
    /// </summary>
    /// <param name="Sku">Stock-keeping unit identifier. Must be 3–64 characters.</param>
    /// <param name="Quantity">Number of units to order. Must be 1–100.</param>
    public sealed record Request(
        [Required, StringLength(64, MinimumLength = 3)] string Sku,
        [Range(1, 100)] int Quantity);

    /// <summary>
    /// Created order response.
    /// </summary>
    /// <param name="Id">Assigned order identifier.</param>
    /// <param name="Sku">Stock-keeping unit identifier.</param>
    /// <param name="Quantity">Number of units ordered.</param>
    public sealed record Response(int Id, string Sku, int Quantity);

    /// <summary>
    /// Stores the order and returns the created resource.
    /// </summary>
    /// <param name="request">Validated request body.</param>
    /// <param name="store">Order store resolved from the per-feature DI container.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created order.</returns>
    public static async Task<Response> Handle(Request request, IOrderStore store, CancellationToken ct)
    {
        var order = await store.AddAsync(request.Sku, request.Quantity, ct).ConfigureAwait(false);
        return new Response(order.Id, order.Sku, order.Quantity);
    }
}

/// <summary>
/// Blocks known-invalid SKUs that DataAnnotations cannot express.
/// Runs after DataAnnotations and before any <c>[Filter&lt;T&gt;]</c> endpoint filters.
/// </summary>
public sealed class CreateOrderValidator : ISliceValidator<CreateOrder.Request>
{
    /// <summary>
    /// Returns a failure when the SKU is on the block-list.
    /// </summary>
    /// <param name="value">The validated request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A success or failure result.</returns>
    public ValueTask<SliceValidationResult> ValidateAsync(CreateOrder.Request value, CancellationToken ct)
        => value.Sku == "blocked-sku"
            ? ValueTask.FromResult(SliceValidationResult.Failure(nameof(value.Sku), "SKU is blocked."))
            : ValueTask.FromResult(SliceValidationResult.Success);
}
