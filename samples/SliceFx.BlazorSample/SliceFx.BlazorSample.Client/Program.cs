using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SliceFx.BlazorSample.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register the DelegatingHandler that attaches a bearer token to every outgoing request.
builder.Services.AddTransient<BearerTokenHandler>();

// Register a named HttpClient and pass the resolved client to the constructor directly.
builder.Services.AddHttpClient(nameof(SliceApiClient),
        c => c.BaseAddress = new Uri("http://localhost:5101"))
    .AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddScoped(sp =>
    new SliceApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SliceApiClient))));

await builder.Build().RunAsync();
