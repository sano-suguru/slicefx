using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Slice.Lambda;

/// <summary>
/// Extension methods for configuring AWS Lambda hosting on a <see cref="WebApplicationBuilder"/>.
/// </summary>
public static class WebApplicationBuilderExtensions
{
    /// <summary>
    /// Configures the application to run as an AWS Lambda function when deployed to Lambda.
    /// When running locally this is a no-op, so the same binary works with both Kestrel
    /// and the Lambda runtime without any code changes.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="eventSource">
    /// The Lambda event source. Defaults to <see cref="LambdaEventSource.HttpApi"/>
    /// (API Gateway HTTP API v2 payload format), which is the recommended choice for
    /// new Lambda-backed APIs.
    /// </param>
    public static WebApplicationBuilder UseSliceLambda(
        this WebApplicationBuilder builder,
        LambdaEventSource eventSource = LambdaEventSource.HttpApi)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddAWSLambdaHosting(eventSource);
        return builder;
    }
}
