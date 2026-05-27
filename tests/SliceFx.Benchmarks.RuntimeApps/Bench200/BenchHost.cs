using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Bench200;

public static class BenchHost
{
    public static void Add(IServiceCollection services) => services.AddSlice();
    public static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapSlices();
}
