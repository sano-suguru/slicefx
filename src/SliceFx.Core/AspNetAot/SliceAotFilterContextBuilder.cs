using Microsoft.AspNetCore.Http;

namespace SliceFx;

/// <summary>
/// Builds a <see cref="SliceFilterContext"/> from an ASP.NET <see cref="HttpContext"/>
/// for use in NativeAOT-safe dispatch of <see cref="ISliceFilter"/> instances.
/// </summary>
/// <remarks>
/// The source generator calls <see cref="Create"/> from generated <c>__AotHandle_*</c>
/// handlers when a feature declares one or more <c>[SliceFilter&lt;T&gt;]</c> attributes and
/// the assembly opts into NativeAOT registration mode via
/// <c>[assembly: SliceAspNetAot]</c>.
/// </remarks>
public static class SliceAotFilterContextBuilder
{
    // HttpContext.Items key for the cached SliceFilterContext.  Using a private static object
    // as the key avoids string key conflicts with other middleware.
    private static readonly object ContextItemsKey = new();

    /// <summary>
    /// Returns the <see cref="SliceFilterContext"/> for the current request, creating it on
    /// the first call and caching it in <see cref="HttpContext.Items"/> for subsequent calls.
    /// </summary>
    /// <remarks>
    /// Caching ensures that all <c>[SliceFilter&lt;T&gt;]</c> filters on a single route
    /// (which are registered as separate <c>AddEndpointFilter</c> calls on the non-AOT path)
    /// share the same <see cref="SliceFilterContext.ResponseHeaders"/> dictionary.
    /// <c>Response.OnStarting</c> is also registered only once per request so that headers
    /// written by any neutral filter are flushed when the response starts — including for
    /// short-circuit (e.g. 429) responses.
    /// </remarks>
    /// <param name="ctx">The current HTTP context.</param>
    /// <returns>The (possibly cached) <see cref="SliceFilterContext"/> for this request.</returns>
    public static SliceFilterContext Create(HttpContext ctx)
    {
        if (ctx.Items.TryGetValue(ContextItemsKey, out var cached) &&
            cached is SliceFilterContext existing)
        {
            return existing;
        }

        var headers = new Dictionary<string, string>(
            ctx.Request.Headers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var header in ctx.Request.Headers)
        {
            headers[header.Key] = header.Value.ToString();
        }

        var routeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ctx.Request.RouteValues)
        {
            routeValues[key] = value?.ToString() ?? string.Empty;
        }

        var filterCtx = new SliceFilterContext(
            ctx.Request.Method,
            ctx.Request.Path.Value ?? string.Empty,
            headers,
            routeValues,
            ctx.RequestServices,
            ctx.Connection.RemoteIpAddress?.ToString(),
            ctx.RequestAborted);

        ctx.Items[ContextItemsKey] = filterCtx;

        // Register OnStarting once so ResponseHeaders are flushed before the response is
        // committed, covering both pass-through and short-circuit paths.
        ctx.Response.OnStarting(static state =>
        {
            var (responseHeaders, response) = ((IDictionary<string, string>, HttpResponse))state;
            foreach (var (key, value) in responseHeaders)
            {
                // Do not overwrite headers already set by the handler or other middleware.
                if (!response.Headers.ContainsKey(key))
                {
                    response.Headers[key] = value;
                }
            }

            return Task.CompletedTask;
        }, (filterCtx.ResponseHeaders, ctx.Response));

        return filterCtx;
    }
}
