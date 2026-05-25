using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;

namespace SliceFx.Lambda.FunctionPerFeature.Tests;

internal static class LambdaTestHelpers
{
    internal static LambdaInvocationContext CreateContext(
        APIGatewayHttpApiV2ProxyRequest? request = null,
        ILambdaContext? lambdaContext = null)
        => new(
            request ?? new APIGatewayHttpApiV2ProxyRequest(),
            new ServiceCollection().BuildServiceProvider(),
            lambdaContext ?? new TestLambdaContext(TimeSpan.FromSeconds(10)),
            CancellationToken.None);
}

internal sealed class TestLambdaContext(TimeSpan remainingTime) : ILambdaContext
{
    public string AwsRequestId => "request-id";

    public IClientContext ClientContext => null!;

    public string FunctionName => "function";

    public string FunctionVersion => "$LATEST";

    public ICognitoIdentity Identity => null!;

    public string InvokedFunctionArn => "arn:aws:lambda:local:000000000000:function:function";

    public ILambdaLogger Logger { get; } = new TestLambdaLogger();

    public string LogGroupName => "log-group";

    public string LogStreamName => "log-stream";

    public int MemoryLimitInMB => 128;

    public TimeSpan RemainingTime { get; } = remainingTime;
}

internal sealed class TestLambdaLogger : ILambdaLogger
{
    public void Log(string message)
    {
    }

    public void LogLine(string message)
    {
    }
}
