using System.Net.Http.Json;
using SliceFx.Sample.Services;
using SliceFx.Testing;

namespace SliceFx.TestHostSample;

// Demonstrates SliceTestHost: starts SliceFx.Sample in-process and drives it over HTTP.
// global::Program refers to SliceFx.Sample's Program (made public via TestSupport.cs).
internal static class Program
{
    internal static async Task Main()
    {
        Console.WriteLine("=== SliceFx.TestHost Demo ===");
        Console.WriteLine();

        var contentRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SliceFx.Sample"));

        await using var host = SliceTestHost.Create<global::Program>(svc =>
            svc.Replace<IUserStore>(new InMemoryUserStore(TimeProvider.System)),
            contentRoot);

        // GET /health
        var health = await host.Client.GetStringAsync("/health").ConfigureAwait(false);
        Console.WriteLine($"GET /health          → {health}");

        // POST /users — valid request
        var created = await host.Client.PostAsJsonAsync(
            "/users", new { name = "Alice", email = "alice@example.com" }).ConfigureAwait(false);
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.WriteLine($"POST /users (valid)  → {(int)created.StatusCode} {created.ReasonPhrase}");
        Console.WriteLine($"                       {body}");

        // POST /users — validation failure
        var bad = await host.Client.PostAsJsonAsync(
            "/users", new { name = "", email = "not-an-email" }).ConfigureAwait(false);
        Console.WriteLine($"POST /users (invalid)→ {(int)bad.StatusCode} {bad.ReasonPhrase}");

        // GET /users — list
        var list = await host.Client.GetStringAsync("/users").ConfigureAwait(false);
        Console.WriteLine($"GET /users           → {list}");

        Console.WriteLine();
        Console.WriteLine("Done.");
    }
}
