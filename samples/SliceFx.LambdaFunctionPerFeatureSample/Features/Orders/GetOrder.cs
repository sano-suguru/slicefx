using Microsoft.AspNetCore.Mvc;
using SliceFx.Lambda.FunctionPerFeature;
using SliceFx.LambdaFunctionPerFeatureSample.Services;

namespace SliceFx.LambdaFunctionPerFeatureSample.Features.Orders;

/// <summary>
/// Retrieves an order by ID. Demonstrates route-token, query-parameter, and
/// header-parameter binding in the function-per-feature Lambda path.
/// </summary>
[Feature("GET /orders/{id:int}", Summary = "Get an order")]
[LambdaFunctionStartup(typeof(OrderFeatureStartup))]
public static class GetOrder
{
    /// <summary>
    /// Order retrieval response.
    /// </summary>
    /// <param name="Id">Order identifier.</param>
    /// <param name="Sku">Stock-keeping unit identifier.</param>
    /// <param name="Quantity">Number of units ordered.</param>
    /// <param name="IncludeDetails">Whether the caller requested extended details.</param>
    /// <param name="TraceId">Caller-supplied trace identifier, if provided.</param>
    public sealed record Response(int Id, string Sku, int Quantity, bool IncludeDetails, string? TraceId);

    /// <summary>
    /// Looks up an order by its integer ID.
    /// </summary>
    /// <param name="id">Order identifier from the route.</param>
    /// <param name="includeDetails">Whether to include extended details (from the <c>details</c> query parameter).</param>
    /// <param name="traceId">Optional trace identifier from the <c>x-trace-id</c> request header.</param>
    /// <param name="store">Order store resolved from the per-feature DI container.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The requested order.</returns>
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
