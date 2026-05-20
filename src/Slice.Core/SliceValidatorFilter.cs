using Microsoft.AspNetCore.Http;

namespace Slice;

/// <summary>
/// Endpoint filter that delegates validation to a DI-resolved <see cref="ISliceValidator{TRequest}"/>.
/// Attach to a feature via <c>[Filter&lt;SliceValidatorFilter&lt;Request&gt;&gt;]</c>.
/// Participates in normal <c>[Filter&lt;T&gt;]</c> declaration order after
/// <see cref="DataAnnotationsValidationFilter"/> is attached.
/// </summary>
/// <typeparam name="TRequest">The request type to validate.</typeparam>
/// <param name="validator">The validator used to validate matching request arguments.</param>
public sealed class SliceValidatorFilter<TRequest>(ISliceValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class
{
    private readonly ISliceValidator<TRequest> _validator = validator;

    /// <summary>
    /// Invokes custom Slice validation for the matching request argument before continuing the endpoint pipeline.
    /// </summary>
    /// <param name="context">The current endpoint filter invocation context.</param>
    /// <param name="next">The next filter or endpoint delegate.</param>
    /// <returns>The endpoint result, or a validation problem response when validation fails.</returns>
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
