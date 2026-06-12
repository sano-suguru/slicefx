using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace SliceFx;

/// <summary>
/// Writes RFC 7807 Problem Details responses for SliceFx NativeAOT-safe ASP.NET dispatch.
/// </summary>
/// <remarks>
/// The source generator calls these methods from generated <c>__AotHandle_*</c> handlers
/// when running in AOT registration mode (opt-in via
/// <c>[assembly: SliceAspNetAot]</c>). Unlike
/// <c>Microsoft.AspNetCore.Http.Results.Problem</c>, these methods serialize via a
/// built-in <see cref="JsonSerializerContext"/> and do not
/// depend on <c>Microsoft.AspNetCore.Http.Json.JsonOptions</c> being configured in the DI
/// container, making them safe under <c>PublishAot=true</c> with an empty HTTP JSON options
/// resolver chain.
/// </remarks>
public static class SliceAotResults
{
    /// <summary>
    /// Writes an RFC 7807 Problem Details response with the specified status, title, and detail.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="title">The short, human-readable problem title.</param>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static async Task Problem(HttpContext httpContext, int status, string title, string? detail)
    {
        var dto = new SliceAotProblemDto("about:blank", title, status, detail, Errors: null);
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            dto,
            SliceAotProblemJsonContext.Default.SliceAotProblemDto,
            contentType: "application/problem+json",
            cancellationToken: httpContext.RequestAborted);
    }

    /// <summary>
    /// Writes an RFC 7807 Problem Details response for validation errors (400).
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="errors">A dictionary of field names to one or more validation messages.</param>
    public static async Task ValidationProblem(
        HttpContext httpContext,
        IReadOnlyDictionary<string, string[]> errors)
    {
        var dto = new SliceAotProblemDto(
            "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            "One or more validation errors occurred.",
            400,
            Detail: null,
            Errors: errors);
        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsJsonAsync(
            dto,
            SliceAotProblemJsonContext.Default.SliceAotProblemDto,
            contentType: "application/problem+json",
            cancellationToken: httpContext.RequestAborted);
    }
}

// Mirrors WasiResults.ProblemDto — a minimal RFC 7807 shape covering both simple problem
// responses and field-keyed validation errors in a single serializable record. Using a
// file-scoped internal record (not nested) so the STJ source generator can process it
// without requiring the containing class to be partial.
internal sealed partial record SliceAotProblemDto(
    string Type,
    string Title,
    int Status,
    string? Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string[]>? Errors);

[JsonSerializable(typeof(SliceAotProblemDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class SliceAotProblemJsonContext : JsonSerializerContext { }
