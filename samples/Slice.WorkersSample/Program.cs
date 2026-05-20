using Microsoft.Extensions.DependencyInjection;
using Slice.Generated;
using Slice.Workers;
using Slice.WorkersSample.Services;

var builder = WorkerHost.CreateBuilder();

// Registers all [Feature] routes via the source generator.
builder.AddSliceGenerated();

// Register application services.
builder.Services.AddSingleton<IClock, SystemClock>();

var app = builder.Build();

// --probe <route>: in-process smoke test (P1 verification).
if (args.Length >= 2 && args[0] == "--probe")
{
    await RunProbeAsync(app, args[1]).ConfigureAwait(false);
    return;
}

// P2: stdin/stdout JSON IPC loop (used by wasmtime / JS shim).
await app.RunAsync().ConfigureAwait(false);

static async Task RunProbeAsync(WorkerApp app, string route)
{
    Console.WriteLine($"[probe] dispatching GET {route}");
    var resp = await app.DispatchAsync(new WorkerRequest("GET", route,
        new Dictionary<string, string>(), null, null)).ConfigureAwait(false);
    Console.WriteLine($"[probe] status={resp.Status} body={System.Text.Encoding.UTF8.GetString(resp.Body)}");

    Console.WriteLine("[probe] dispatching POST /echo {Message:'hello'}");
    var body = System.Text.Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"message":"hello"}""");
    var echoResp = await app.DispatchAsync(new WorkerRequest("POST", "/echo",
        new Dictionary<string, string> { ["Content-Type"] = "application/json" }, null, body)).ConfigureAwait(false);
    Console.WriteLine($"[probe] status={echoResp.Status} body={System.Text.Encoding.UTF8.GetString(echoResp.Body)}");

    Console.WriteLine("[probe] dispatching POST /echo {} (should fail validation)");
    var badBody = System.Text.Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"message":""}""");
    var badResp = await app.DispatchAsync(new WorkerRequest("POST", "/echo",
        new Dictionary<string, string> { ["Content-Type"] = "application/json" }, null, badBody)).ConfigureAwait(false);
    Console.WriteLine($"[probe] status={badResp.Status} body={System.Text.Encoding.UTF8.GetString(badResp.Body)}");
}
