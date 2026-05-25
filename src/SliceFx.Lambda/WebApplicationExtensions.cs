using Microsoft.AspNetCore.Builder;

namespace SliceFx.Lambda;

/// <summary>
/// Extension methods for running a Slice application on AWS Lambda.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Runs the application. When deployed to Lambda the Lambda runtime drives the event loop;
    /// when running locally Kestrel starts normally. Call after <see cref="WebApplicationBuilderExtensions.UseSliceLambda"/>.
    /// </summary>
    /// <param name="app">The configured web application.</param>
    /// <returns>A task that represents the lifetime of the application.</returns>
    public static Task RunOnLambdaAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.RunAsync();
    }
}
