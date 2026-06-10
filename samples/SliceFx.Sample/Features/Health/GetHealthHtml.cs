namespace SliceFx.Sample.Features.Health;

/// <summary>
/// Demonstrates <see cref="SliceResult.Html"/> — returns an HTML health page.
/// </summary>
[Feature("GET /health/html", Summary = "HTML health page (SliceResult.Html demo)")]
public static class GetHealthHtml
{
    /// <summary>
    /// Returns a minimal HTML page to demonstrate <c>SliceResult.Html</c>.
    /// </summary>
    public static SliceResult Handle(TimeProvider timeProvider)
    {
        var html = $"""
            <!DOCTYPE html>
            <html><head><title>SliceFx Health</title></head>
            <body>
              <h1>SliceFx is healthy</h1>
              <p>Time: {timeProvider.GetUtcNow():O}</p>
            </body>
            </html>
            """;
        return SliceResult.Html(html);
    }
}
