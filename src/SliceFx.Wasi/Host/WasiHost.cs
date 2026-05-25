namespace SliceFx.Wasi;

/// <summary>
/// Entry point for creating Slice WASI applications.
/// </summary>
public static class WasiHost
{
    /// <summary>
    /// Creates a new <see cref="WasiHostBuilder"/> for registering services and WASI routes.
    /// </summary>
    /// <returns>A new WASI host builder.</returns>
    public static WasiHostBuilder CreateBuilder() => new();
}
