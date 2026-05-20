using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Slice.Core.Tests.Features.Validation;

namespace Slice.Core.Tests;

public class DataAnnotationsValidationFilterTests
{
    [Fact]
    public async Task InvokeAsync_validates_record_primary_constructor_attributes()
    {
        var handle = typeof(CreateValidatedItem).GetMethod(nameof(CreateValidatedItem.Handle))!;
        var services = CreateServices();
        var filter = DataAnnotationsValidationFilter.Create(handle, services);

        Assert.NotNull(filter);

        var result = await filter.InvokeAsync(
            CreateContext(services, new CreateValidatedItem.Request("A")),
            _ => throw new InvalidOperationException("The endpoint should not run when validation fails."));

        var (statusCode, body) = await ExecuteResultAsync(result, services);

        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
        Assert.Contains("Name", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_continues_when_request_is_valid()
    {
        var handle = typeof(CreateValidatedItem).GetMethod(nameof(CreateValidatedItem.Handle))!;
        var services = CreateServices();
        var filter = DataAnnotationsValidationFilter.Create(handle, services);
        var called = false;

        Assert.NotNull(filter);

        var result = await filter.InvokeAsync(
            CreateContext(services, new CreateValidatedItem.Request("Valid")),
            _ =>
            {
                called = true;
                return ValueTask.FromResult<object?>("next");
            });

        Assert.True(called);
        Assert.Equal("next", result);
    }

    [Fact]
    public void Create_ignores_service_parameters()
    {
        var handle = typeof(UsesServiceParameter).GetMethod(nameof(UsesServiceParameter.Handle))!;
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TestDependency>()
            .BuildServiceProvider();

        var filter = DataAnnotationsValidationFilter.Create(handle, services);

        Assert.Null(filter);
    }

    private static DefaultEndpointFilterInvocationContext CreateContext(IServiceProvider services, params object?[] arguments)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
        };

        return new DefaultEndpointFilterInvocationContext(httpContext, arguments);
    }

    private static ServiceProvider CreateServices()
        => new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(object? result, IServiceProvider services)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
        };
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        var httpResult = Assert.IsType<IResult>(result, exactMatch: false);
        await httpResult.ExecuteAsync(httpContext);

        body.Position = 0;
        using var reader = new StreamReader(body);
        return (httpContext.Response.StatusCode, await reader.ReadToEndAsync());
    }
}
