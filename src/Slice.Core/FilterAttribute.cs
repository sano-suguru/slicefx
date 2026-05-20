namespace Slice;

internal interface IFilterAttribute
{
    Type FilterType { get; }
}

/// <summary>
/// Attaches an ASP.NET Core endpoint filter to a Slice feature.
/// </summary>
/// <typeparam name="TFilter">
/// The endpoint filter type to resolve from dependency injection for each request.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FilterAttribute<TFilter> : Attribute, IFilterAttribute
    where TFilter : class, Microsoft.AspNetCore.Http.IEndpointFilter
{
    /// <summary>
    /// Gets the concrete endpoint filter type declared by this attribute.
    /// </summary>
    public Type FilterType => typeof(TFilter);
}
