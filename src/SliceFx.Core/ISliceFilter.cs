// CA1711: SliceFilterDelegate intentionally ends in 'Delegate' — same convention as
//         EndpointFilterDelegate in ASP.NET Core; the suffix signals the pipeline-step signature.
// CA1716: 'next' as a parameter name is the accepted convention for middleware/filter pipelines
//         in both ASP.NET Core and general .NET. The VB.NET 'Next' keyword conflict is acceptable.
#pragma warning disable CA1711, CA1716

namespace SliceFx;

/// <summary>
/// A delegate that represents the next step in the neutral filter pipeline.
/// </summary>
/// <param name="context">The neutral filter context for the current request.</param>
/// <returns>A <see cref="SliceFilterResult"/> representing the outcome of the pipeline step.</returns>
public delegate ValueTask<SliceFilterResult> SliceFilterDelegate(SliceFilterContext context);

/// <summary>
/// Defines a host-neutral filter that can intercept requests on both the ASP.NET Core
/// and WASI dispatch paths.
/// </summary>
/// <remarks>
/// <para>
/// Implement <see cref="ISliceFilter"/> when the same filter logic should run on both the
/// ASP.NET Core and WASI dispatch paths. For filters that require ASP.NET-specific
/// capabilities (access to bound arguments, response header mutation, etc.), use
/// <see cref="Microsoft.AspNetCore.Http.IEndpointFilter"/> via
/// <see cref="FilterAttribute{TFilter}"/> instead.
/// </para>
/// <para>
/// Neutral filters are declared with <c>[SliceFilter&lt;T&gt;]</c> on a feature class and
/// execute <em>before</em> DataAnnotations and <c>ISliceValidator&lt;T&gt;</c> validation —
/// so an API key check or rate limiter can short-circuit before body deserialization occurs.
/// </para>
/// <para>
/// v1 constraints:
/// <list type="bullet">
///   <item><description>The filter context exposes only <c>Method</c>, <c>Path</c>,
///     <c>Headers</c>, <c>RouteValues</c>, <c>Services</c>, and <c>CancellationToken</c>.
///     Bound request body arguments are not available (body binding on the WASI path happens
///     after the filter chain).</description></item>
///   <item><description>Short-circuit responses use the body-less <see cref="SliceResult"/>
///     struct (Problem Details: status + title + optional detail). For structured
///     field-level validation errors use <c>ISliceValidator&lt;T&gt;</c> instead.</description></item>
///   <item><description>Post-handler response header <em>addition</em> is supported via
///     <see cref="SliceFilterContext.ResponseHeaders"/>; response body mutation is not
///     supported. Single-valued headers only; multi-valued headers such as
///     <c>Set-Cookie</c> require <c>IEndpointFilter</c> on ASP.NET.
///     <see cref="SliceFilterResult.Status"/> provides read-only status observation after
///     the inner pipeline completes.</description></item>
///   <item><description><see cref="SliceFilterContext.ClientIp"/> is populated on the ASP.NET
///     host (from <c>HttpContext.Connection.RemoteIpAddress</c>, respecting
///     <c>UseForwardedHeaders</c>), but is always <c>null</c> on the WASI host because
///     <c>wasi:http@0.2</c> does not expose peer address information.</description></item>
/// </list>
/// </para>
/// </remarks>
public interface ISliceFilter
{
    /// <summary>
    /// Invokes the filter logic.
    /// </summary>
    /// <param name="context">The neutral filter context for the current request.</param>
    /// <param name="next">The delegate that invokes the next step in the pipeline.</param>
    /// <returns>A <see cref="SliceFilterResult"/> representing the final outcome.</returns>
    ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next);
}

/// <summary>
/// Provides host-neutral request metadata to a <see cref="ISliceFilter"/> implementation.
/// </summary>
/// <remarks>
/// The context exposes only primitive, host-neutral data. Bound request body arguments are
/// not available because body deserialization on the WASI dispatch path occurs after the filter
/// chain completes. <see cref="RouteValues"/> keys and values are always <see cref="string"/>;
/// typed route constraint values (e.g., <c>{id:int}</c>) are exposed as their string
/// representation.
/// </remarks>
public sealed class SliceFilterContext
{
    /// <summary>Gets the HTTP method (upper-case, e.g. <c>"GET"</c>, <c>"POST"</c>).</summary>
    public string Method { get; }

    /// <summary>Gets the request path (e.g. <c>"/users/123"</c>).</summary>
    public string Path { get; }

    /// <summary>
    /// Gets the request headers. Key comparison is case-insensitive on both WASI and ASP.NET hosts.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>
    /// Gets the route parameter values captured from the matched route pattern.
    /// Values are always <see cref="string"/>; typed route constraint values (e.g.,
    /// <c>{id:int}</c>) are exposed as their string representation.
    /// </summary>
    public IReadOnlyDictionary<string, string> RouteValues { get; }

    /// <summary>Gets the scoped service provider for the current request.</summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the validated remote client IP address, or <c>null</c> when the host cannot determine it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the ASP.NET host this is sourced from <c>HttpContext.Connection.RemoteIpAddress</c>.
    /// If the application has configured <c>UseForwardedHeaders</c>, that middleware runs before
    /// the filter pipeline and rewrites <c>RemoteIpAddress</c> to the de-forwarded client IP,
    /// so <see cref="ClientIp"/> transparently reflects that configuration.
    /// </para>
    /// <para>
    /// On the WASI host this property is always <c>null</c>: the <c>wasi:http@0.2</c> incoming
    /// handler interface does not expose peer address information.
    /// </para>
    /// <para>
    /// <strong>Note:</strong> Filters that key on <see cref="ClientIp"/> (e.g. rate limiters)
    /// will see every request as <c>null</c>/"unknown" on the WASI host and therefore cannot
    /// enforce per-client limits on that path.
    /// </para>
    /// </remarks>
    public string? ClientIp { get; }

    /// <summary>Gets the cancellation token associated with the current request.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets a writable dictionary of headers to add to the response.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entries written here are merged into the host response before it is sent:
    /// on the ASP.NET path via <c>Response.OnStarting</c>, and on the WASI path via
    /// header merge into the final <c>WasiResponse</c>.  This applies to both short-circuit
    /// and pass-through results, so a filter that returns a 429 can still include
    /// <c>Retry-After</c>.
    /// </para>
    /// <para>
    /// Only single-valued headers are supported.  For multi-valued headers such as
    /// <c>Set-Cookie</c>, use host-specific APIs (<c>IEndpointFilter</c> on ASP.NET).
    /// </para>
    /// <para>
    /// Key comparison is case-insensitive.  Entries written by filter code take precedence
    /// over headers already set by the handler for absent keys; existing handler-set headers
    /// are not overwritten on the WASI path.
    /// </para>
    /// </remarks>
    public IDictionary<string, string> ResponseHeaders { get; }

    /// <summary>
    /// Initializes a new <see cref="SliceFilterContext"/> with the specified request metadata.
    /// </summary>
    public SliceFilterContext(
        string method,
        string path,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyDictionary<string, string> routeValues,
        IServiceProvider services,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        Method = method;
        Path = path;
        Headers = headers;
        RouteValues = routeValues;
        Services = services;
        ClientIp = clientIp;
        CancellationToken = cancellationToken;
        ResponseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The result of a <see cref="ISliceFilter"/> invocation.
/// Carries either a short-circuit error response or a host-specific pass-through value
/// produced by the inner pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Factory methods:
/// <list type="bullet">
///   <item><description><see cref="ShortCircuit"/> — terminates the pipeline early with a
///     host-neutral error response expressed as a body-less <see cref="SliceResult"/>
///     (Problem Details: status + title + optional detail). Host adapters translate this to a
///     Problem Details response body.</description></item>
///   <item><description><see cref="PassThrough"/> — carries the inner pipeline's response for
///     observation-only filters. The <see cref="HostResponse"/> field holds the host-specific
///     response value boxed as <see cref="object"/>.</description></item>
/// </list>
/// </para>
/// <para>
/// The <see cref="Status"/> field is <see cref="Nullable{T}"/> (best-effort):
/// WASI path — always set from <c>WasiResponse.Status</c>; ASP.NET path — set when the
/// inner result is a <see cref="SliceResult"/> or implements
/// <c>Microsoft.AspNetCore.Http.IStatusCodeHttpResult</c>; <c>null</c> for raw POCO results
/// or other <c>IResult</c> types.
/// </para>
/// </remarks>
public readonly struct SliceFilterResult
{
    private SliceFilterResult(
        bool isShortCircuit,
        SliceResult? shortCircuitResult,
        object? hostResponse,
        int? status)
    {
        IsShortCircuit = isShortCircuit;
        ShortCircuitResult = shortCircuitResult;
        HostResponse = hostResponse;
        Status = status;
    }

    /// <summary>
    /// <c>true</c> when the filter short-circuited the pipeline; <c>false</c> for a
    /// pass-through result produced by the inner handler.
    /// </summary>
    public bool IsShortCircuit { get; }

    /// <summary>
    /// The error result provided to <see cref="ShortCircuit"/> when the filter terminated
    /// the pipeline early; <c>null</c> for pass-through results.
    /// </summary>
    public SliceResult? ShortCircuitResult { get; }

    /// <summary>
    /// The host-specific response value produced by the inner pipeline, boxed as
    /// <see cref="object"/>. <c>null</c> for short-circuit results.
    /// Concrete type is host-dependent (e.g., <c>SliceFx.Wasi.WasiResponse</c> on the WASI
    /// path, <c>object?</c> on the ASP.NET path).
    /// </summary>
    public object? HostResponse { get; }

    /// <summary>
    /// The HTTP status code of the result (best-effort). Always present for short-circuit
    /// and WASI pass-through results; may be <c>null</c> on the ASP.NET path for POCO or
    /// unknown <c>IResult</c> return types.
    /// </summary>
    public int? Status { get; }

    /// <summary>
    /// Creates a short-circuit result that terminates the filter pipeline with a host-neutral
    /// error response.
    /// </summary>
    /// <param name="result">
    /// The body-less <see cref="SliceResult"/> that describes the error. Host adapters convert
    /// this to a Problem Details response body. For structured field-level validation errors,
    /// use <c>ISliceValidator&lt;T&gt;</c> instead.
    /// </param>
    /// <example>
    /// <code>
    /// // Short-circuit with 401 Unauthorized
    /// return SliceFilterResult.ShortCircuit(SliceResult.Unauthorized("Invalid API key."));
    /// </code>
    /// </example>
    public static SliceFilterResult ShortCircuit(SliceResult result) =>
        new(isShortCircuit: true, shortCircuitResult: result, hostResponse: null, status: result.Status);

    /// <summary>
    /// Creates a pass-through result carrying the host-specific response from the inner pipeline.
    /// Used by the source-generated dispatch code; filter implementations typically return the
    /// value received from their <c>next</c> delegate directly.
    /// </summary>
    /// <param name="hostResponse">
    /// The host-specific response value, boxed as <see cref="object"/>. On the WASI path this
    /// is a <c>WasiResponse</c>; on the ASP.NET path it is the raw <c>object?</c> returned by
    /// <c>EndpointFilterDelegate</c>.
    /// </param>
    /// <param name="status">
    /// The HTTP status code (best-effort). Pass <c>null</c> when the status cannot be determined
    /// without inspecting the host-specific response type.
    /// </param>
    public static SliceFilterResult PassThrough(object? hostResponse, int? status) =>
        new(isShortCircuit: false, shortCircuitResult: null, hostResponse: hostResponse, status: status);
}
