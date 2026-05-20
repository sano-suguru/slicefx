namespace Slice.Sample.Filters;

/// <summary>
/// Demonstrates a per-feature auth filter. Requires the request to carry
/// <c>X-API-Key: secret</c>. Returns 401 otherwise.
/// </summary>
public sealed class RequireApiKeyFilter : IEndpointFilter
{
    private const string HeaderName = "X-API-Key";
    private const string ExpectedKey = "secret"; // demo only — read from IConfiguration in real code

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        return !context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var key) || key != ExpectedKey
            ? Results.Unauthorized()
            : await next(context).ConfigureAwait(false);
    }
}
