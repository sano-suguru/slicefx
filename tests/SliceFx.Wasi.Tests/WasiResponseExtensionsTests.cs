namespace SliceFx.Wasi.Tests;

public class WasiResponseExtensionsTests
{
    [Fact]
    public void ToSliceFilterResult_wraps_WasiResponse_as_pass_through()
    {
        var wasiResp = new WasiResponse(200, new Dictionary<string, string>(), []);
        var result = wasiResp.ToSliceFilterResult();

        Assert.False(result.IsShortCircuit);
        Assert.Null(result.ShortCircuitResult);
        Assert.Same(wasiResp, result.HostResponse);
        Assert.Equal(200, result.Status);
    }

    [Fact]
    public void ToSliceFilterResult_preserves_status_from_WasiResponse()
    {
        var wasiResp = new WasiResponse(404, new Dictionary<string, string>(), []);
        var result = wasiResp.ToSliceFilterResult();

        Assert.Equal(404, result.Status);
    }
}
