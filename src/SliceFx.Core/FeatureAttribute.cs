namespace SliceFx;

/// <summary>
/// Marks a class as a Slice feature. The class must contain a public static <c>Handle</c> method
/// whose parameters are bound automatically (body, route, query, services).
/// </summary>
/// <example>
/// <code>
/// [Feature("POST /users")]
/// public static class CreateUser
/// {
///     public record Request(string Name, string Email);
///     public record Response(Guid Id);
///
///     public static async Task&lt;Response&gt; Handle(Request req, IUserStore store, CancellationToken ct)
///     {
///         var id = await store.AddAsync(req.Name, req.Email, ct);
///         return new Response(id);
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class FeatureAttribute : Attribute
{
    /// <summary>
    /// Gets the HTTP method parsed from <see cref="Route"/>.
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// Gets the route pattern parsed from <see cref="Route"/>.
    /// </summary>
    public string Pattern { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FeatureAttribute"/> class.
    /// </summary>
    /// <param name="route">Route in "METHOD /path" form, e.g. "POST /users" or "GET /users/{id:guid}".</param>
    public FeatureAttribute(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        var parts = route.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Route must be in 'METHOD /path' form, got '{route}'", nameof(route));
        }

        Route = route;
        Method = parts[0].ToUpperInvariant();
        Pattern = parts[1];
    }

    /// <summary>Optional OpenAPI tag. Defaults to the namespace segment after "Features".</summary>
    public string? Tag { get; set; }

    /// <summary>Optional endpoint name. Defaults to "{Tag}.{FeatureClassName}".</summary>
    public string? Name { get; set; }

    /// <summary>Optional summary for OpenAPI / documentation.</summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Gets the original route declaration in "METHOD /path" form.
    /// </summary>
    public string Route { get; }
}
