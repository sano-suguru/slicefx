using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Core.Tests;

public class SliceValidatorFilterTests
{
    [Fact]
    public async Task InvokeAsync_returns_validation_problem_when_validator_fails()
    {
        var services = CreateServices();
        var filter = new SliceValidatorFilter<CustomRequest>(new RejectingValidator());

        var result = await filter.InvokeAsync(
            CreateContext(services, new CustomRequest("blocked")),
            _ => throw new InvalidOperationException("The endpoint should not run when validation fails."));

        var (statusCode, body) = await ExecuteResultAsync(result, services);

        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
        Assert.Contains("Name", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_continues_when_validator_succeeds()
    {
        var services = CreateServices();
        var filter = new SliceValidatorFilter<CustomRequest>(new AcceptingValidator());
        var called = false;

        var result = await filter.InvokeAsync(
            CreateContext(services, new CustomRequest("allowed")),
            _ =>
            {
                called = true;
                return ValueTask.FromResult<object?>("next");
            });

        Assert.True(called);
        Assert.Equal("next", result);
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

    private sealed record CustomRequest(string Name);

    private sealed class RejectingValidator : ISliceValidator<CustomRequest>
    {
        public ValueTask<SliceValidationResult> ValidateAsync(CustomRequest value, CancellationToken ct)
            => ValueTask.FromResult(SliceValidationResult.Failure("Name", "Name is blocked."));
    }

    private sealed class AcceptingValidator : ISliceValidator<CustomRequest>
    {
        public ValueTask<SliceValidationResult> ValidateAsync(CustomRequest value, CancellationToken ct)
            => ValueTask.FromResult(SliceValidationResult.Success);
    }
}
