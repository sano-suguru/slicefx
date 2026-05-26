using Microsoft.AspNetCore.Http;

namespace SliceFx.Lambda.NativeAotFixture.Features.Orders;

[Feature("GET /orders/aspnet-only")]
public static class AspNetOnlyOrder
{
    public static IResult Handle() => Results.Ok("aspnet-only");
}
