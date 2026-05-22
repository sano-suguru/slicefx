using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;

namespace Slice.Lambda.PerFunction;

/// <summary>
/// Provides request, service, Lambda, and cancellation state to generated per-feature handlers.
/// </summary>
public sealed class LambdaInvocationContext(
    APIGatewayHttpApiV2ProxyRequest request,
    IServiceProvider services,
    ILambdaContext lambdaContext,
    CancellationToken cancellationToken)
{
    /// <summary>
    /// Gets the API Gateway HTTP API v2 request.
    /// </summary>
    public APIGatewayHttpApiV2ProxyRequest Request { get; } = request;

    /// <summary>
    /// Gets the scoped service provider for this invocation.
    /// </summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>
    /// Gets the AWS Lambda context.
    /// </summary>
    public ILambdaContext LambdaContext { get; } = lambdaContext;

    /// <summary>
    /// Gets the cancellation token synthesized for this invocation.
    /// </summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;
}
