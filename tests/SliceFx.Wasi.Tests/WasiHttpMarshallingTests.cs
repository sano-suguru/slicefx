using System.Text;

namespace SliceFx.Wasi.Tests;

public sealed class WasiHttpMarshallingTests
{
    // ── SplitPathAndQuery ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/items/1", "/items/1", null)]
    [InlineData("/items?page=2", "/items", "page=2")]
    [InlineData("/search?q=hello&limit=10", "/search", "q=hello&limit=10")]
    [InlineData("/?", "/", "")]
    public void SplitPathAndQuery_splits_path_and_query(string input, string expectedPath, string? expectedQuery)
    {
        WasiHttpMarshalling.SplitPathAndQuery(input, out var path, out var query);

        Assert.Equal(expectedPath, path);
        Assert.Equal(expectedQuery, query);
    }

    // ── ParseHeaders ─────────────────────────────────────────────────────────────

    [Fact]
    public void ParseHeaders_decodes_utf8_values()
    {
        var entries = new (string, byte[])[]
        {
            ("Content-Type", Encoding.UTF8.GetBytes("application/json")),
            ("X-Custom", Encoding.UTF8.GetBytes("value")),
        };

        var headers = WasiHttpMarshalling.ParseHeaders(entries);

        Assert.Equal("application/json", headers["Content-Type"]);
        Assert.Equal("value", headers["X-Custom"]);
    }

    [Fact]
    public void ParseHeaders_is_case_insensitive()
    {
        var entries = new (string, byte[])[]
        {
            ("content-type", Encoding.UTF8.GetBytes("text/html")),
        };

        var headers = WasiHttpMarshalling.ParseHeaders(entries);

        Assert.Equal("text/html", headers["CONTENT-TYPE"]);
        Assert.Equal("text/html", headers["content-type"]);
    }

    [Fact]
    public void ParseHeaders_falls_back_to_latin1_for_non_utf8_values()
    {
        // 0xFF is valid Latin-1 but invalid UTF-8.
        var entries = new (string, byte[])[]
        {
            ("X-Bin", [0xFF]),
        };

        var headers = WasiHttpMarshalling.ParseHeaders(entries);

        Assert.True(headers.ContainsKey("X-Bin"));
        Assert.NotEmpty(headers["X-Bin"]);
    }

    [Fact]
    public void ParseHeaders_returns_empty_dict_for_empty_input()
    {
        var headers = WasiHttpMarshalling.ParseHeaders([]);
        Assert.Empty(headers);
    }

    // ── IsBodySizeWithinLimit ────────────────────────────────────────────────────

    [Fact]
    public void IsBodySizeWithinLimit_returns_true_when_header_absent()
    {
        var result = WasiHttpMarshalling.IsBodySizeWithinLimit(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            maxBodyBytes: 1024);

        Assert.True(result);
    }

    [Fact]
    public void IsBodySizeWithinLimit_returns_true_when_content_length_within_limit()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Length"] = "512",
        };

        Assert.True(WasiHttpMarshalling.IsBodySizeWithinLimit(headers, 1024));
    }

    [Fact]
    public void IsBodySizeWithinLimit_returns_true_exactly_at_limit()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Length"] = "1024",
        };

        Assert.True(WasiHttpMarshalling.IsBodySizeWithinLimit(headers, 1024));
    }

    [Fact]
    public void IsBodySizeWithinLimit_returns_false_when_content_length_exceeds_limit()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Length"] = "1025",
        };

        Assert.False(WasiHttpMarshalling.IsBodySizeWithinLimit(headers, 1024));
    }

    [Fact]
    public void IsBodySizeWithinLimit_returns_true_when_content_length_is_not_a_number()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Length"] = "chunked",
        };

        Assert.True(WasiHttpMarshalling.IsBodySizeWithinLimit(headers, 1024));
    }

    // ── FormatResponseHeaders ────────────────────────────────────────────────────

    [Fact]
    public void FormatResponseHeaders_lowercases_names_and_encodes_values_as_utf8()
    {
        var input = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Content-Type"] = "application/json",
            ["X-Custom-Header"] = "hello",
        };

        var result = WasiHttpMarshalling.FormatResponseHeaders(input);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Name == "content-type" && Encoding.UTF8.GetString(e.Value) == "application/json");
        Assert.Contains(result, e => e.Name == "x-custom-header" && Encoding.UTF8.GetString(e.Value) == "hello");
    }

    [Fact]
    public void FormatResponseHeaders_returns_empty_list_for_empty_input()
    {
        var result = WasiHttpMarshalling.FormatResponseHeaders(
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Empty(result);
    }
}
