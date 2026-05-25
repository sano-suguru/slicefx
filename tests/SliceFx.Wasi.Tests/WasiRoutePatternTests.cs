using SliceFx.Wasi.Routing;

namespace SliceFx.Wasi.Tests;

public class WasiRoutePatternTests
{
    [Fact]
    public void TryMatch_treats_escaped_braces_as_literals()
    {
        var pattern = new WasiRoutePattern("/diagnostics/{{name}}");

        var matched = pattern.TryMatch("/diagnostics/{name}", out var routeValues);

        Assert.True(matched);
        Assert.Empty(routeValues);
    }

    [Fact]
    public void TryMatch_treats_single_braces_as_parameters()
    {
        var pattern = new WasiRoutePattern("/users/{id}");

        var matched = pattern.TryMatch("/users/42", out var routeValues);

        Assert.True(matched);
        Assert.Equal("42", routeValues["id"]);
    }
}
