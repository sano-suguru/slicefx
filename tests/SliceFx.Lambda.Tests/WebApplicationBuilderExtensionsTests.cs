using Microsoft.AspNetCore.Builder;
using LambdaEventSource = Microsoft.Extensions.DependencyInjection.LambdaEventSource;

namespace SliceFx.Lambda.Tests;

public sealed class WebApplicationBuilderExtensionsTests
{
    [Fact]
    public void UseSliceLambda_throws_for_null_builder()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WebApplicationBuilderExtensions.UseSliceLambda(null!));
    }

    [Fact]
    public void UseSliceLambda_returns_same_builder_in_lambda_environment()
    {
        WithLambdaEnvironment(static () =>
        {
            var builder = WebApplication.CreateSlimBuilder();

            var returned = builder.UseSliceLambda();

            Assert.Same(builder, returned);
        });
    }

    [Fact]
    public void UseSliceLambda_accepts_custom_event_source()
    {
        var builder = WebApplication.CreateSlimBuilder();

        var returned = builder.UseSliceLambda(LambdaEventSource.RestApi);

        Assert.Same(builder, returned);
    }

    private static void WithLambdaEnvironment(Action action)
    {
        var taskRoot = Environment.GetEnvironmentVariable("LAMBDA_TASK_ROOT");
        var runtimeApi = Environment.GetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API");
        try
        {
            Environment.SetEnvironmentVariable("LAMBDA_TASK_ROOT", "/tmp/slice-lambda-test");
            Environment.SetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API", "127.0.0.1:9001");
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("LAMBDA_TASK_ROOT", taskRoot);
            Environment.SetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API", runtimeApi);
        }
    }
}
