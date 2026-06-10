namespace SliceFx.Core.Tests;

public sealed class HttpsUrlAttributeTests
{
    private static readonly HttpsUrlAttribute s_attr = new();

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/path?query=1")]
    [InlineData("https://sub.domain.example.com:8443/resource")]
    public void IsValid_accepts_https_urls(string url)
        => Assert.True(s_attr.IsValid(url));

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("ftp://files.example.com")]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData("//example.com")]
    public void IsValid_rejects_non_https_and_invalid_strings(string url)
        => Assert.False(s_attr.IsValid(url));

    [Fact]
    public void IsValid_accepts_null()
        => Assert.True(s_attr.IsValid(null));

    [Fact]
    public void FormatErrorMessage_includes_property_name()
    {
        var msg = s_attr.FormatErrorMessage("FeedUrl");
        Assert.Contains("FeedUrl", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_ErrorMessage_is_returned_by_FormatErrorMessage()
    {
        var custom = new HttpsUrlAttribute { ErrorMessage = "Must be HTTPS: {0}" };
        Assert.Equal("Must be HTTPS: Url", custom.FormatErrorMessage("Url"));
    }
}
