using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Slice.Core.Tests.Features.Products;

namespace Slice.Core.Tests;

public class SliceExtensionsTests
{
    [Fact]
    public void AddSlice_registers_declared_filters_as_scoped_services()
    {
        var services = new ServiceCollection();

        services.AddSlice(typeof(GetProduct).Assembly);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(RecordingFilter));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void MapSlices_maps_feature_route_and_metadata()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSlice(typeof(GetProduct).Assembly);
        using var app = builder.Build();

        app.MapSlices(typeof(GetProduct).Assembly);

        var endpoint = Assert.IsType<RouteEndpoint>(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).Single(
                e => e is RouteEndpoint routeEndpoint && routeEndpoint.RoutePattern.RawText == "/products/{id:guid}"));

        Assert.Equal("/products/{id:guid}", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods);
        Assert.Equal("Products.GetProduct", endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Equal(["Products"], endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags);
        Assert.Equal("Get a product", endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary);
    }
}
