using Microsoft.AspNetCore.Http;

namespace Slice;

/// <summary>
/// Endpoint filter that delegates validation to a DI-resolved <see cref="ISliceValidator{TRequest}"/>.
/// Attach to a feature via <c>[Filter&lt;SliceValidatorFilter&lt;Request&gt;&gt;]</c>.
/// Participates in normal <c>[Filter&lt;T&gt;]</c> declaration order after
/// <see cref="DataAnnotationsValidationFilter"/> is attached.
/// </summary>
public sealed class SliceValidatorFilter<TRequest>(ISliceValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class
{
    private readonly ISliceValidator<TRequest> _validator = validator;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        for (var i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] is not TRequest request)
            {
                continue;
            }

            var result = await _validator.ValidateAsync(request, context.HttpContext.RequestAborted).ConfigureAwait(false);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(
                    result.Errors!.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal));
            }
        }

        return await next(context).ConfigureAwait(false);
    }
}
