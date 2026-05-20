namespace Slice;

internal interface IFilterAttribute
{
    Type FilterType { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FilterAttribute<TFilter> : Attribute, IFilterAttribute
    where TFilter : class, Microsoft.AspNetCore.Http.IEndpointFilter
{
    public Type FilterType => typeof(TFilter);
}
