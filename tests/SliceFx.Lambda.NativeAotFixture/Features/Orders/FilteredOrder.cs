using Microsoft.AspNetCore.Http;

namespace SliceFx.Lambda.NativeAotFixture.Features.Orders;

[Feature("GET /orders/filtered")]
[Filter<AuditFilter>]
public static class FilteredOrder
{
    public static string Handle() => "filtered";
}

public sealed class AuditFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        => next(context);
}
