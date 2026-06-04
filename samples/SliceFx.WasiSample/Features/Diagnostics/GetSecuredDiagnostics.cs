using SliceFx.WasiSample.Filters;

namespace SliceFx.WasiSample.Features.Diagnostics;

/// <summary>
/// Returns diagnostic metadata. Protected by the <see cref="ApiKeyFilter"/> neutral filter
/// and observed by the <see cref="RequestTimingFilter"/> — both run on the WASI dispatch path.
/// This feature is classified as <c>portable</c> in the route manifest because it uses only
/// <c>[SliceFilter&lt;T&gt;]</c> attributes (no ASP.NET <c>[Filter&lt;T&gt;]</c>).
/// </summary>
[Feature("GET /diagnostics/secured", Summary = "Secured diagnostics endpoint (neutral filter demo)")]
[SliceFilter<RequestTimingFilter>]
[SliceFilter<ApiKeyFilter>]
public static class GetSecuredDiagnostics
{
    /// <summary>
    /// Diagnostic payload.
    /// </summary>
    /// <param name="Version">Runtime version string.</param>
    /// <param name="Timestamp">UTC time at request processing.</param>
    public record Response(string Version, DateTimeOffset Timestamp);

    /// <summary>
    /// Returns diagnostic information. Requires the <c>X-Api-Key</c> header.
    /// </summary>
    public static Response Handle(TimeProvider timeProvider)
        => new(typeof(GetSecuredDiagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0",
               timeProvider.GetUtcNow());
}
