namespace SliceFx.BlazorSample.Client;

// Attaches a placeholder bearer token to every outgoing API request.
// Replace with a real authentication handler (e.g. MSAL, IdentityModel) in production.
internal sealed class BearerTokenHandler : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "demo-token");
        return base.SendAsync(request, cancellationToken);
    }
}
