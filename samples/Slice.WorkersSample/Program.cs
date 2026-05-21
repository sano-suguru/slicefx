using Microsoft.Extensions.DependencyInjection;
using Slice.Workers;

var builder = WorkerHost.CreateBuilder();

// Registers all [Feature] routes via the source generator.
builder.AddSlice();

// Register application services.
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// --probe <route>: in-process smoke test (P1 verification).
if (args.Length >= 2 && args[0] == "--probe")
{
    RunProbe(app, args[1]);
    return;
}

// P2: stdin/stdout JSON IPC loop (used by wasmtime / JS shim).
app.Run();

static void RunProbe(WorkerApp app, string route)
{
    Console.WriteLine($"[probe] dispatching GET {route}");
    var resp = app.DispatchAsync(new WorkerRequest("GET", route,
        new Dictionary<string, string>(), null, null)).GetAwaiter().GetResult();
    Console.WriteLine($"[probe] status={resp.Status} body={System.Text.Encoding.UTF8.GetString(resp.Body)}");

    Console.WriteLine("[probe] dispatching POST /echo {Message:'hello'}");
    var body = System.Text.Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"message":"hello"}""");
    var echoResp = app.DispatchAsync(new WorkerRequest("POST", "/echo",
        new Dictionary<string, string> { ["Content-Type"] = "application/json" }, null, body)).GetAwaiter().GetResult();
    Console.WriteLine($"[probe] status={echoResp.Status} body={System.Text.Encoding.UTF8.GetString(echoResp.Body)}");

    Console.WriteLine("[probe] dispatching POST /echo malformed JSON (should fail bad request)");
    var malformedBody = System.Text.Encoding.UTF8.GetBytes("{");
    var malformedResp = app.DispatchAsync(new WorkerRequest("POST", "/echo",
        new Dictionary<string, string> { ["Content-Type"] = "application/json" }, null, malformedBody)).GetAwaiter().GetResult();
    var malformedResponseBody = System.Text.Encoding.UTF8.GetString(malformedResp.Body);
    Console.WriteLine($"[probe] status={malformedResp.Status} body={malformedResponseBody}");
    if (malformedResp.Status != 400 || !malformedResponseBody.Contains("malformed JSON", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Malformed JSON probe failed.");
    }

    Console.WriteLine("[probe] dispatching POST /echo {} (should fail validation)");
    var badBody = System.Text.Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"message":""}""");
    var badResp = app.DispatchAsync(new WorkerRequest("POST", "/echo",
        new Dictionary<string, string> { ["Content-Type"] = "application/json" }, null, badBody)).GetAwaiter().GetResult();
    Console.WriteLine($"[probe] status={badResp.Status} body={System.Text.Encoding.UTF8.GetString(badResp.Body)}");

    Console.WriteLine("[probe] dispatching GET /nested-generic");
    var nestedResp = app.DispatchAsync(new WorkerRequest("GET", "/nested-generic",
        new Dictionary<string, string>(), null, null)).GetAwaiter().GetResult();
    var nestedBody = System.Text.Encoding.UTF8.GetString(nestedResp.Body);
    Console.WriteLine($"[probe] status={nestedResp.Status} body={nestedBody}");
    if (nestedResp.Status != 200 || !nestedBody.Contains("values", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Nested generic response probe failed.");
    }

    Console.WriteLine("[probe] dispatching POST /validation-fallback (should use reflection fallback)");
    var fallbackBody = System.Text.Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"name":"","items":[1]}""");
    var fallbackResp = app.DispatchAsync(new WorkerRequest("POST", "/validation-fallback",
        new Dictionary<string, string> { ["Content-Type"] = "application/json" }, null, fallbackBody)).GetAwaiter().GetResult();
    var fallbackResponseBody = System.Text.Encoding.UTF8.GetString(fallbackResp.Body);
    Console.WriteLine($"[probe] status={fallbackResp.Status} body={fallbackResponseBody}");
    if (fallbackResp.Status != 400
        || !fallbackResponseBody.Contains("Name is required by custom validation.", StringComparison.Ordinal)
        || !fallbackResponseBody.Contains("minimum length", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Validation fallback probe failed.");
    }

    Console.WriteLine("[probe] dispatching POST /array-min-length (should use generated array validation)");
    var arrayBody = System.Text.Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"items":[1]}""");
    var arrayResp = app.DispatchAsync(new WorkerRequest("POST", "/array-min-length",
        new Dictionary<string, string> { ["Content-Type"] = "application/json" }, null, arrayBody)).GetAwaiter().GetResult();
    var arrayResponseBody = System.Text.Encoding.UTF8.GetString(arrayResp.Body);
    Console.WriteLine($"[probe] status={arrayResp.Status} body={arrayResponseBody}");
    if (arrayResp.Status != 400 || !arrayResponseBody.Contains("minimum length", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Array MinLength probe failed.");
    }
}
