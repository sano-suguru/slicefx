using Microsoft.Extensions.DependencyInjection;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using SliceFx.Wasi.KeyValue;

namespace SliceFx.WasiSample;

/// <summary>
/// Builds the WASI application for this sample. Extracted here (compiled in every build) so the
/// wasm entry point and the in-process test project share one wiring definition and cannot drift.
/// </summary>
public static class SampleWasiApp
{
    /// <summary>
    /// Demo URL that the seeded in-memory HTTP client answers with canned HTML. It is NOT a real
    /// outbound request — on Spin / Cloudflare, <see cref="IWasiHttpClient"/> is backed by a
    /// wasi:http/outgoing-handler implementation (see the slicefx-inbox app for the WIT-bound layer).
    /// </summary>
    public const string DemoFetchUrl = "https://slicefx.example/hello";

    /// <summary>
    /// Builds a fresh <see cref="WasiApp"/> with the sample's service wiring. Returns a NEW app on
    /// every call (no cached singleton) so in-process tests get isolated in-memory state; the wasm
    /// entry point caches its own singleton. Pass overrides to inject test doubles.
    /// </summary>
    public static WasiApp Create(
        IKeyValueStore? keyValueStore = null,
        IWasiHttpClient? httpClient = null)
    {
        var builder = WasiHost.CreateBuilder();
        builder.AddSlice();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.AddKeyValueStore(keyValueStore ?? new InMemoryKeyValueStore());
        builder.AddWasiHttpClient(httpClient ?? CreateSeededHttpClient());
        return builder.Build();
    }

    // Demo/test double: canned HTML for DemoFetchUrl only. Real outbound HTTP is host-provided.
    private static InMemoryWasiHttpClient CreateSeededHttpClient() =>
        new InMemoryWasiHttpClient().Respond(
            request => request.Url == DemoFetchUrl,
            new WasiResponse(
                200,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "text/html" },
                "<html><head><title>Hello from SliceFx</title></head><body>hi</body></html>"u8.ToArray()));
}
