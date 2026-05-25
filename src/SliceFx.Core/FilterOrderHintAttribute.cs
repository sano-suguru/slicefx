namespace SliceFx;

/// <summary>
/// Declares an ordering preference between this endpoint filter and another, so the Slice
/// source generator can warn (SLICE010) when a feature's <c>[Filter&lt;T&gt;]</c> declarations
/// place filters in an order that contradicts the declared preference.
/// </summary>
/// <remarks>
/// Apply this attribute to an <c>IEndpointFilter</c> implementation. The Slice source generator
/// reads it during compilation; it has no runtime behavior of its own. Only <see cref="After"/>
/// is honored today — <see cref="Before"/> is reserved for symmetry but treated as a hint that
/// the partner filter should set <see cref="After"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FilterOrderHintAttribute : Attribute
{
    /// <summary>
    /// Declares that the annotated filter must appear AFTER the given filter type in any
    /// <c>[Filter&lt;T&gt;]</c> declaration sequence on a Slice feature.
    /// </summary>
    public Type? After { get; init; }

    /// <summary>
    /// Declares that the annotated filter must appear BEFORE the given filter type. Reserved
    /// for future analyzer support; today it is not enforced — set <see cref="After"/> on the
    /// partner filter instead.
    /// </summary>
    public Type? Before { get; init; }
}
