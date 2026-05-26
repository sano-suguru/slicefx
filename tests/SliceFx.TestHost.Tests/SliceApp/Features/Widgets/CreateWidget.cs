using System.ComponentModel.DataAnnotations;
using SliceFx.TestHost.SliceApp.Services;

namespace SliceFx.TestHost.SliceApp.Features.Widgets;

[Feature("POST /widgets", Summary = "Create a widget")]
[Filter<ResponseHeaderFilter>]
public static class CreateWidget
{
    public sealed record Request([Required, MinLength(2)] string Name);

    public sealed record Response(int Id, string Name, string Source);

    public static Response Handle(Request request, IWidgetStore store)
    {
        var widget = store.Add(request.Name);
        return new Response(widget.Id, widget.Name, "store");
    }
}

public sealed class CreateWidgetValidator : ISliceValidator<CreateWidget.Request>
{
    public ValueTask<SliceValidationResult> ValidateAsync(CreateWidget.Request value, CancellationToken ct)
    {
        if (string.Equals(value.Name, "blocked", StringComparison.Ordinal))
        {
            return ValueTask.FromResult(SliceValidationResult.Failure(nameof(value.Name), "Name is blocked."));
        }

        if (string.Equals(value.Name, "x", StringComparison.Ordinal))
        {
            return ValueTask.FromResult(SliceValidationResult.Failure(nameof(value.Name), "Name failed Slice validator."));
        }

        return ValueTask.FromResult(SliceValidationResult.Success);
    }
}

public sealed class ResponseHeaderFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context).ConfigureAwait(false);
        context.HttpContext.Response.Headers["X-Slice-Filter"] = "executed";
        return result;
    }
}
