namespace SliceFx.TestHost.SliceApp.Features.Widgets;

[Feature("GET /widgets/html")]
public static class HtmlWidget
{
    public static SliceResult Handle()
        => SliceResult.Html("<h1>Hello from SliceFx</h1>");
}
