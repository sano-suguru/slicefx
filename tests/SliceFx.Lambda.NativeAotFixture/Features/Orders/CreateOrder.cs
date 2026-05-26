using System.ComponentModel.DataAnnotations;
using SliceFx.Lambda.FunctionPerFeature;
using SliceFx.Lambda.NativeAotFixture.Services;

namespace SliceFx.Lambda.NativeAotFixture.Features.Orders;

[Feature("POST /orders", Summary = "Create an order")]
[LambdaFunctionStartup(typeof(OrderFeatureStartup))]
public static class CreateOrder
{
    public sealed record Request(
        [Required, StringLength(64, MinimumLength = 3)] string Sku,
        [Range(1, 100)] int Quantity);

    public sealed record Response(int Id, string Sku, int Quantity);

    public static async Task<Response> Handle(Request request, IOrderStore store, CancellationToken ct)
    {
        var order = await store.AddAsync(request.Sku, request.Quantity, ct).ConfigureAwait(false);
        return new Response(order.Id, order.Sku, order.Quantity);
    }
}

public sealed class CreateOrderValidator : ISliceValidator<CreateOrder.Request>
{
    public ValueTask<SliceValidationResult> ValidateAsync(CreateOrder.Request value, CancellationToken ct)
        => value.Sku == "blocked-sku"
            ? ValueTask.FromResult(SliceValidationResult.Failure(nameof(value.Sku), "SKU is blocked."))
            : ValueTask.FromResult(SliceValidationResult.Success);
}
