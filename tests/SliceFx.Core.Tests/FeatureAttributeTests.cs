namespace SliceFx.Core.Tests;

public sealed class FeatureAttributeTests
{
    [Fact]
    public void Constructor_parses_route_and_normalizes_method()
    {
        var attribute = new FeatureAttribute("post   /widgets/{id:int}");

        Assert.Equal("post   /widgets/{id:int}", attribute.Route);
        Assert.Equal("POST", attribute.Method);
        Assert.Equal("/widgets/{id:int}", attribute.Pattern);
    }

    [Fact]
    public void Constructor_rejects_null_or_whitespace_route()
    {
        Assert.Throws<ArgumentNullException>(() => new FeatureAttribute(null!));
        Assert.Throws<ArgumentException>(() => new FeatureAttribute("   "));
    }

    [Fact]
    public void Constructor_rejects_route_without_method_and_pattern()
    {
        var exception = Assert.Throws<ArgumentException>(() => new FeatureAttribute("GET"));

        Assert.Contains("METHOD /path", exception.Message, StringComparison.Ordinal);
    }
}
