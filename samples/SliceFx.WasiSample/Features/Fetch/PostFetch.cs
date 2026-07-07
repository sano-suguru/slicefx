using System.ComponentModel.DataAnnotations;
using System.Text;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;

namespace SliceFx.WasiSample.Features.Fetch;

/// <summary>
/// Fetches a URL through <see cref="IWasiHttpClient"/> and extracts its HTML title — a miniature
/// of the slicefx-inbox og:title use case. In this sample the client is an in-memory double with
/// a canned response; on Spin / Cloudflare it is backed by wasi:http/outgoing-handler.
/// </summary>
[Feature("POST /fetch", Summary = "Fetch a URL and extract its title")]
public static class PostFetch
{
    /// <summary>Request body for the fetch endpoint.</summary>
    /// <param name="Url">Absolute URL to fetch.</param>
    public record Request([Required, Url] string Url);

    /// <summary>Result of the fetch.</summary>
    /// <param name="Url">The requested URL.</param>
    /// <param name="Status">Upstream HTTP status code.</param>
    /// <param name="Title">Extracted &lt;title&gt; text, or null when absent.</param>
    public record Response(string Url, int Status, string? Title);

    /// <summary>Performs the outbound GET and maps upstream failures to 502.</summary>
    public static async Task<SliceResult<Response>> Handle(Request req, IWasiHttpClient http, CancellationToken ct)
    {
        WasiResponse upstream;
        try
        {
            upstream = await http.SendAsync(new WasiHttpRequest("GET", req.Url, Headers: null, Body: null), ct);
        }
        catch (WasiHttpException ex)
        {
            return SliceResult<Response>.Problem(502, "Bad Gateway", ex.Message);
        }

        if (upstream.Status is < 200 or > 299)
        {
            return SliceResult<Response>.Problem(502, "Bad Gateway", $"Upstream returned {upstream.Status}.");
        }

        return SliceResult<Response>.Ok(new Response(req.Url, upstream.Status, ExtractTitle(upstream.Body)));
    }

    // Minimal, dependency-free title extraction (no regex, no crypto) — WASI-safe.
    private static string? ExtractTitle(byte[] body)
    {
        var html = Encoding.UTF8.GetString(body);
        var open = html.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
        if (open < 0)
        {
            return null;
        }

        var start = open + "<title>".Length;
        var close = html.IndexOf("</title>", start, StringComparison.OrdinalIgnoreCase);
        return close < 0 ? null : html[start..close].Trim();
    }
}
