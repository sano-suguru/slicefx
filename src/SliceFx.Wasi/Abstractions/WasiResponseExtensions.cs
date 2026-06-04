namespace SliceFx.Wasi;

/// <summary>
/// Extension methods that translate <see cref="WasiResponse"/> values to
/// <see cref="SliceFilterResult"/> instances.
/// </summary>
/// <remarks>
/// These methods are called by source-generated WASI dispatch code when neutral filters
/// (<c>[SliceFilter&lt;T&gt;]</c>) are present on a feature. Each early-return path in the
/// generated handler core delegates to this helper so that the filter chain can observe the
/// response status after the inner pipeline completes.
/// </remarks>
public static class WasiResponseExtensions
{
    /// <summary>
    /// Wraps a <see cref="WasiResponse"/> as a pass-through <see cref="SliceFilterResult"/>
    /// so it can travel through the neutral filter chain.
    /// </summary>
    /// <param name="response">The WASI response produced by the inner handler pipeline.</param>
    /// <returns>
    /// A <see cref="SliceFilterResult"/> with <see cref="SliceFilterResult.IsShortCircuit"/>
    /// set to <c>false</c> and <see cref="SliceFilterResult.Status"/> set to the response
    /// status code.
    /// </returns>
    public static SliceFilterResult ToSliceFilterResult(this WasiResponse response) =>
        SliceFilterResult.PassThrough(response, response.Status);
}
