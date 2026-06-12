using SliceFx.AotSample.Services;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSlice();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITodoStore, InMemoryTodoStore>();

var app = builder.Build();

app.MapSlices();

app.Run();
