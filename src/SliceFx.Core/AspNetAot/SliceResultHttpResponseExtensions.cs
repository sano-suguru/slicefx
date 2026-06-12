using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace SliceFx;

/// <summary>
/// Extension methods for executing <see cref="SliceResult"/> and <see cref="SliceResult{T}"/>
/// values as ASP.NET HTTP responses in NativeAOT-safe dispatch.
/// </summary>
/// <remarks>
/// The source generator calls these extension methods from generated <c>__AotHandle_*</c>
/// handlers when the assembly opts into AOT registration mode via
/// <c>[assembly: SliceAspNetAot]</c>. These methods write responses directly to
/// <see cref="HttpContext.Response"/> using <see cref="JsonTypeInfo{T}"/> for serialization,
/// avoiding any per-request reflection.
/// </remarks>
public static class SliceResultHttpResponseExtensions
{
    /// <summary>
    /// Executes a <see cref="SliceResult{T}"/> by writing the appropriate HTTP response.
    /// </summary>
    /// <typeparam name="T">The payload type for success responses with a body.</typeparam>
    /// <param name="result">The slice result to execute.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="jsonTypeInfo">
    /// The JSON type metadata used to serialize the success body. Resolved by the
    /// generator from the <c>[SliceJsonContext(SliceJsonTarget.AspNet)]</c> context.
    /// </param>
    public static async Task ExecuteAsync<T>(
        this SliceResult<T> result,
        HttpContext httpContext,
        JsonTypeInfo<T> jsonTypeInfo)
    {
        if (result.IsSuccess && result.HasBody)
        {
            httpContext.Response.StatusCode = result.Status;
            if (result.Location is not null)
            {
                httpContext.Response.Headers.Location = result.Location;
            }

            // Value is guaranteed non-null when HasBody is true (invariant of SliceResult<T> factory methods).
            await httpContext.Response.WriteAsJsonAsync(
                result.Value!,
                jsonTypeInfo,
                cancellationToken: httpContext.RequestAborted);
        }
        else if (result.IsSuccess)
        {
            httpContext.Response.StatusCode = result.Status;
            if (result.Location is not null)
            {
                httpContext.Response.Headers.Location = result.Location;
            }
        }
        else
        {
            await SliceAotResults.Problem(
                httpContext,
                result.Status,
                result.ProblemTitle ?? result.Status.ToString(System.Globalization.CultureInfo.InvariantCulture),
                result.ProblemDetail);
        }
    }

    /// <summary>
    /// Executes a non-generic <see cref="SliceResult"/> by writing the appropriate HTTP response.
    /// </summary>
    /// <param name="result">The slice result to execute.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    public static async Task ExecuteAsync(this SliceResult result, HttpContext httpContext)
    {
        switch (result.Kind)
        {
            case SliceResultKind.Redirect:
                httpContext.Response.StatusCode = result.Status;
                if (result.Location is not null)
                {
                    httpContext.Response.Headers.Location = result.Location;
                }

                break;

            case SliceResultKind.RawBody:
                httpContext.Response.StatusCode = result.Status;
                if (result.ContentType is not null)
                {
                    httpContext.Response.ContentType = result.ContentType;
                }

                if (result.Body is not null)
                {
                    await httpContext.Response.Body.WriteAsync(result.Body, httpContext.RequestAborted);
                }

                break;

            case SliceResultKind.StatusOnly:
            default:
                if (!result.IsSuccess && result.ProblemTitle is not null)
                {
                    await SliceAotResults.Problem(
                        httpContext,
                        result.Status,
                        result.ProblemTitle,
                        result.ProblemDetail);
                }
                else
                {
                    httpContext.Response.StatusCode = result.Status;
                    if (result.Location is not null)
                    {
                        httpContext.Response.Headers.Location = result.Location;
                    }
                }

                break;
        }
    }
}
