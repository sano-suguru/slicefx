using Microsoft.AspNetCore.Http;

namespace Slice.Core.Tests.Features.Products;

[Feature("GET /products/{id:guid}", Summary = "Get a product")]
[Filter<RecordingFilter>]
public static class GetProduct
{
    public static IResult Handle(Guid id) => Results.Ok(new Response(id));

    public sealed record Response(Guid Id);
}

public sealed class RecordingFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        => next(context);
}
