using Slice.Generated;
using Slice.Sample.Services;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSliceGenerated();
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();

var app = builder.Build();

app.MapSlicesGenerated(); // <-- ここで全Featureが自動登録される

app.Run();
