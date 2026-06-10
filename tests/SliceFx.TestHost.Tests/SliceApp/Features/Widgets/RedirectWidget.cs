namespace SliceFx.TestHost.SliceApp.Features.Widgets;

[Feature("GET /widgets/redirect")]
public static class RedirectWidget
{
    public static SliceResult Handle()
        => SliceResult.Redirect("/widgets/1");
}
