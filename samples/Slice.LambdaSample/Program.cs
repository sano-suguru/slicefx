using Slice;
using Slice.Lambda;

var builder = WebApplication.CreateSlimBuilder(args);

// Registers all features, filters, and validators discovered by the source generator.
builder.Services.AddSlice();
builder.Services.AddSingleton(TimeProvider.System);

// Configures Lambda hosting when running in AWS Lambda; no-op locally (Kestrel handles it).
builder.UseSliceLambda();

var app = builder.Build();

// Maps all [Feature]-annotated endpoints.
app.MapSlices();

// In Lambda: processes events via the Lambda runtime.
// Locally: starts Kestrel on the configured port (see appsettings.json).
await app.RunOnLambdaAsync().ConfigureAwait(false);
