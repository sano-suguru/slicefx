using SliceFx.BlazorSample.Server.Services;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();

// Allow the Blazor WASM client (http://localhost:5102) to call this API.
// AllowAnyHeader is required: the BearerTokenHandler adds Authorization, which triggers
// an OPTIONS preflight that must be explicitly permitted.
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
        p.WithOrigins("http://localhost:5102")
         .AllowAnyHeader()
         .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.MapSlices();
app.Run();
