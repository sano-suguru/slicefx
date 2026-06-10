namespace SliceFx.Sample.Services;

/// <summary>
/// Scoped holder for the resolved user identity.
/// Populated by <see cref="Filters.UserAuthFilter"/> before the handler runs;
/// registered with a factory lambda so activation never touches reflection.
/// </summary>
public sealed class CurrentUser
{
    /// <summary>Gets or sets the authenticated user name.</summary>
    public string Name { get; set; } = string.Empty;
}
