using SliceFx;
using SliceFx.TestHost.SliceApp.Services;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddSlice();
builder.Services.AddSingleton<IWidgetStore, InMemoryWidgetStore>();

var app = builder.Build();
app.MapSlices();
app.Run();
