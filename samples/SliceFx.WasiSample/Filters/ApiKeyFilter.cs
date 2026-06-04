namespace SliceFx.WasiSample.Filters;

/// <summary>
/// A host-neutral API key filter that runs on both the ASP.NET Core and WASI dispatch paths.
/// Demonstrates the short-circuit pattern: returns 401 Unauthorized before body deserialization
/// and validation occur when the <c>X-Api-Key</c> header is absent or wrong.
/// </summary>
/// <remarks>
/// Use <c>[SliceFilter&lt;ApiKeyFilter&gt;]</c> on features that require this check.
/// Because <c>ApiKeyFilter</c> implements <see cref="ISliceFilter"/> (not
/// <c>IEndpointFilter</c>), it is portable — features using it exclusively are classified as
/// <c>portable</c> in the route manifest rather than <c>partial</c>.
/// </remarks>
public sealed class ApiKeyFilter : ISliceFilter
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ExpectedKey = "wasi-demo-key";

    /// <inheritdoc />
    public ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
    {
        if (!context.Headers.TryGetValue(ApiKeyHeader, out var key) || key != ExpectedKey)
        {
            return ValueTask.FromResult(
                SliceFilterResult.ShortCircuit(
                    SliceResult.Unauthorized($"Expected '{ApiKeyHeader}: {ExpectedKey}'.")));
        }

        return next(context);
    }
}
