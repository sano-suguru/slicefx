namespace SliceFx;

/// <summary>
/// Attaches a host-neutral Slice filter to a Slice feature.
/// </summary>
/// <typeparam name="TFilter">
/// The neutral filter type to resolve from dependency injection for each request.
/// Must implement <see cref="ISliceFilter"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// Use <c>[SliceFilter&lt;T&gt;]</c> when the same filter logic should run on both the
/// ASP.NET Core and WASI dispatch paths. For filters that require ASP.NET-specific
/// capabilities (access to bound arguments, response header mutation, etc.), use
/// <see cref="FilterAttribute{TFilter}"/> (<c>[Filter&lt;T&gt;]</c>) instead.
/// </para>
/// <para>
/// Features that use only <c>[SliceFilter&lt;T&gt;]</c> (no <c>[Filter&lt;T&gt;]</c>) are
/// classified as <c>portable</c> in the route manifest, provided they also satisfy the other
/// WASI portability requirements. Features mixing both attribute types are classified as
/// <c>partial</c> because the ASP.NET-specific filters do not run on the WASI dispatch path.
/// </para>
/// <para>
/// Execution order within a feature:
/// <c>[SliceFilter&lt;T&gt;]</c> (declaration order, outermost first) →
/// DataAnnotations / <c>ISliceValidator&lt;T&gt;</c> →
/// <c>[Filter&lt;T&gt;]</c> (declaration order, outermost first) → handler.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SliceFilterAttribute<TFilter> : Attribute
    where TFilter : class, ISliceFilter
{
    /// <summary>
    /// Gets the concrete neutral filter type declared by this attribute.
    /// </summary>
    public Type FilterType => typeof(TFilter);
}
