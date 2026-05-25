using SliceFx.TestHost.TestApp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IMessageService, DefaultMessageService>();

var app = builder.Build();

app.MapGet("/message", (IMessageService service) => service.Message);
app.MapGet("/content-root", (IWebHostEnvironment environment) => environment.ContentRootPath);

app.Run();
