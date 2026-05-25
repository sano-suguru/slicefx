namespace SliceFx.Sample.Filters;

/// <summary>
/// Demonstrates a per-feature endpoint filter. Requires the request to carry
/// <c>X-API-Key: secret</c>. Use ASP.NET Core Authorization for production auth policies.
/// </summary>
public sealed class RequireApiKeyFilter : IEndpointFilter
{
    private const string HeaderName = "X-API-Key";
    private const string ExpectedKey = "secret"; // demo only — read from IConfiguration in real code

    /// <summary>
    /// Allows the request to continue only when the demo API key header is present.
    /// </summary>
    /// <param name="context">Current endpoint invocation context.</param>
    /// <param name="next">Next delegate in the endpoint filter pipeline.</param>
    /// <returns><c>401 Unauthorized</c> or the result produced by the remaining pipeline.</returns>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        return !context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var key) || key != ExpectedKey
            ? Results.Unauthorized()
            : await next(context).ConfigureAwait(false);
    }
}
