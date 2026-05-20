using System.Reflection;
using System.Reflection.Emit;
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

    [Fact]
    public void MapSlices_rejects_duplicate_endpoint_names_across_calls()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSlice(typeof(GetProduct).Assembly);
        using var app = builder.Build();

        app.MapSlices(typeof(GetProduct).Assembly);

        var ex = Assert.Throws<InvalidOperationException>(() => app.MapSlices(typeof(GetProduct).Assembly));
        Assert.Contains("Duplicate Slice endpoint name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_fallback_rejects_overloaded_handle_methods_with_clear_error()
    {
        var assembly = CreateFeatureAssembly("OverloadedFeatureAssembly", defineHandles: static type =>
        {
            DefineStringHandle(type, Type.EmptyTypes);
            DefineStringHandle(type, [typeof(int)]);
        });

        var builder = WebApplication.CreateSlimBuilder();
        using var app = builder.Build();

        var ex = Assert.Throws<InvalidOperationException>(() => app.MapSlices(assembly));

        Assert.Contains("defines multiple public static 'Handle' methods", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_fallback_rejects_missing_handle_methods_with_clear_error()
    {
        var assembly = CreateFeatureAssembly("MissingFeatureAssembly", defineHandles: static _ => { });

        var builder = WebApplication.CreateSlimBuilder();
        using var app = builder.Build();

        var ex = Assert.Throws<InvalidOperationException>(() => app.MapSlices(assembly));

        Assert.Contains("must define exactly one public static 'Handle' method", ex.Message, StringComparison.Ordinal);
    }

    private static AssemblyBuilder CreateFeatureAssembly(string name, Action<TypeBuilder> defineHandles)
    {
        var assemblyName = new AssemblyName(name);
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var module = assemblyBuilder.DefineDynamicModule(name);
        var type = module.DefineType(
            "DynamicTest.Features.Products.DynamicFeature",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        var featureCtor = typeof(FeatureAttribute).GetConstructor([typeof(string)])
            ?? throw new InvalidOperationException("FeatureAttribute constructor was not found.");
        type.SetCustomAttribute(new CustomAttributeBuilder(featureCtor, ["GET /dynamic"]));

        defineHandles(type);
        type.CreateType();
        return assemblyBuilder;
    }

    private static void DefineStringHandle(TypeBuilder type, Type[] parameters)
    {
        var method = type.DefineMethod(
            "Handle",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            parameters);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "ok");
        il.Emit(OpCodes.Ret);
    }
}
