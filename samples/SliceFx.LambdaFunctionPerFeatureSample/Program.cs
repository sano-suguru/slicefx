using SliceFx;
using SliceFx.Lambda;
using SliceFx.LambdaFunctionPerFeatureSample.Services;

var builder = WebApplication.CreateSlimBuilder(args);

// Registers all features, filters, validators, and per-feature startup types discovered by the source generator.
builder.Services.AddSlice();

// Register the demo store for local development (Kestrel).
// In function-per-feature Lambda, each artifact's DI is configured by OrderFeatureStartup instead.
builder.Services.AddSingleton<IOrderStore, InMemoryOrderStore>();

// Configures Lambda hosting when running in AWS Lambda; no-op locally (Kestrel handles it).
// For per-feature NativeAOT packaging, use:
//   slicefx package aws-lambda --mode function-per-feature --rid linux-x64
builder.UseSliceLambda();

var app = builder.Build();

// Maps all [Feature]-annotated endpoints.
app.MapSlices();

// In Lambda: processes events via the Lambda runtime.
// Locally: starts Kestrel on http://localhost:5000 (default).
await app.RunOnLambdaAsync().ConfigureAwait(false);
