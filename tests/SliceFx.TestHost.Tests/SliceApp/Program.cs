using SliceFx;
using SliceFx.TestHost.SliceApp.Services;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddSlice();
builder.Services.AddSingleton<IWidgetStore, InMemoryWidgetStore>();
builder.Services.AddSingleton<AuditRecorder>();
builder.Services.AddKeyedSingleton<IClock, PromotionClock>("promotion");

var app = builder.Build();
app.MapSlices();
app.Run();
