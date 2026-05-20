using Microsoft.AspNetCore.Builder;

namespace Slice.Lambda;

/// <summary>
/// Extension methods for running a Slice application on AWS Lambda.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Runs the application. When deployed to Lambda the Lambda runtime drives the event loop;
    /// when running locally Kestrel starts normally. Call after <see cref="WebApplicationBuilderExtensions.UseSliceLambda"/>.
    /// </summary>
    public static Task RunOnLambdaAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.RunAsync();
    }
}
