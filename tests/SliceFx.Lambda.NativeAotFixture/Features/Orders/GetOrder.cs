using Microsoft.AspNetCore.Mvc;
using SliceFx.Lambda.FunctionPerFeature;
using SliceFx.Lambda.NativeAotFixture.Services;

namespace SliceFx.Lambda.NativeAotFixture.Features.Orders;

[Feature("GET /orders/{id:int}", Summary = "Get an order")]
[LambdaFunctionStartup(typeof(OrderFeatureStartup))]
public static class GetOrder
{
    public sealed record Response(int Id, string Sku, int Quantity, bool IncludeDetails, string? TraceId);

    public static async Task<Response> Handle(
        int id,
        [FromQuery(Name = "details")] bool includeDetails,
        [FromHeader(Name = "x-trace-id")] string? traceId,
        IOrderStore store,
        CancellationToken ct)
    {
        var order = await store.GetAsync(id, ct).ConfigureAwait(false);
        return new Response(order.Id, order.Sku, order.Quantity, includeDetails, traceId);
    }
}
