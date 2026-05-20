using System.Diagnostics;

namespace Slice.Sample.Filters;

/// <summary>
/// Logs request start, end, elapsed time, and the response status code.
/// </summary>
public sealed partial class RequestLoggingFilter(ILogger<RequestLoggingFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var endpoint = context.HttpContext.GetEndpoint();
        var name = endpoint?.Metadata.GetMetadata<RouteNameMetadata>()?.RouteName
                   ?? endpoint?.DisplayName
                   ?? "(anonymous)";

        var sw = Stopwatch.StartNew();
        LogStart(logger, name, context.HttpContext.Request.Method, context.HttpContext.Request.Path);

        try
        {
            var result = await next(context).ConfigureAwait(false);
            sw.Stop();
            var status = InferStatus(result, context.HttpContext.Response.StatusCode);
            LogEnd(logger, name, status, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogError(logger, ex, name, sw.ElapsedMilliseconds);
            throw;
        }
    }

    private static int InferStatus(object? result, int fallback) => result switch
    {
        IStatusCodeHttpResult sc => sc.StatusCode ?? fallback,
        _ => fallback,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = ">> {Feature} {Method} {Path}")]
    private static partial void LogStart(ILogger logger, string feature, string method, PathString path);

    [LoggerMessage(Level = LogLevel.Information, Message = "<< {Feature} -> {Status} in {Elapsed}ms")]
    private static partial void LogEnd(ILogger logger, string feature, int status, long elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "!! {Feature} threw after {Elapsed}ms")]
    private static partial void LogError(ILogger logger, Exception ex, string feature, long elapsed);
}
