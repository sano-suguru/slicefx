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
    /// <summary>
    /// Creates a <see cref="SliceFilterContext"/> from the current <see cref="HttpContext"/>.
    /// Converts <c>IHeaderDictionary</c> to
    /// <c>IReadOnlyDictionary&lt;string, string&gt;</c> and
    /// <c>RouteValueDictionary</c> to
    /// <c>IReadOnlyDictionary&lt;string, string&gt;</c> as required by
    /// <see cref="SliceFilterContext"/>.
    /// </summary>
    /// <param name="ctx">The current HTTP context.</param>
    /// <returns>A <see cref="SliceFilterContext"/> populated from the request.</returns>
    public static SliceFilterContext Create(HttpContext ctx)
    {
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

        return new SliceFilterContext(
            ctx.Request.Method,
            ctx.Request.Path.Value ?? string.Empty,
            headers,
            routeValues,
            ctx.RequestServices,
            ctx.RequestAborted);
    }
}
