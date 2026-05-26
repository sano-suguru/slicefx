using SliceFx.Sample.Services;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();
builder.Services.AddSingleton<AuditLog>();
builder.Services.AddKeyedSingleton<IClock, SystemClock>("promotion");

var app = builder.Build();

app.MapSlices(); // <-- Registers all features automatically
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();
