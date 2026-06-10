namespace SliceFx.Sample.Features.Health;

/// <summary>
/// Demonstrates <see cref="SliceResult.Redirect"/> — redirects <c>/health/v1</c> to <c>/health</c>.
/// </summary>
[Feature("GET /health/v1", Summary = "Redirect to current health endpoint (SliceResult.Redirect demo)")]
public static class GetHealthRedirect
{
    /// <summary>
    /// Permanently redirects callers of the legacy <c>/health/v1</c> endpoint to <c>/health</c>.
    /// </summary>
    public static SliceResult Handle()
        => SliceResult.Redirect("/health", permanent: true);
}
