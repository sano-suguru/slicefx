using Slice;
using Slice.Generated;
using Slice.Lambda;
using Slice.LambdaSample.Features.Echo;
using Slice.LambdaSample.Validators;

var builder = WebApplication.CreateSlimBuilder(args);

// Registers all features and filters discovered by the source generator.
builder.Services.AddSliceGenerated();

// ISliceValidator<T> implementations are registered manually (not auto-scanned).
builder.Services.AddScoped<ISliceValidator<PostEcho.Request>, EchoRequestValidator>();

// Configures Lambda hosting when running in AWS Lambda; no-op locally (Kestrel handles it).
builder.UseSliceLambda();

var app = builder.Build();

// Maps all [Feature]-annotated endpoints.
app.MapSlicesGenerated();

// In Lambda: processes events via the Lambda runtime.
// Locally: starts Kestrel on the configured port (see appsettings.json).
await app.RunOnLambdaAsync().ConfigureAwait(false);
