using Microsoft.Extensions.DependencyInjection;
using SliceFx.Lambda.FunctionPerFeature;
using SliceFx.Lambda.NativeAotFixture.Services;

namespace SliceFx.Lambda.NativeAotFixture.Features.Orders;

public sealed class OrderFeatureStartup : ILambdaFunctionPerFeatureStartup
{
    public void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IOrderStore, InMemoryOrderStore>();
}
