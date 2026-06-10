using SliceFx.Sample.Services;

namespace SliceFx.Sample.Filters;

/// <summary>
/// Demonstrates host-neutral authentication via <see cref="ISliceFilter"/>:
/// validates a bearer token, then stores the resolved identity in the scoped
/// <see cref="CurrentUser"/> service so handlers can inject it directly.
/// Works on ASP.NET, WASI, and Lambda without modification.
/// </summary>
/// <remarks>
/// The <see cref="CurrentUser"/> scoped service is registered with a factory lambda
/// in Program.cs to avoid <c>ActivatorUtilities</c> reflection under full-trim NativeAOT.
/// See <c>docs/guides/aot-safe-scoped-di.md</c> for the canonical pattern.
/// </remarks>
public sealed class UserAuthFilter : ISliceFilter
{
    // Demo token map — read from IConfiguration / a token store in real code.
    private static readonly Dictionary<string, string> s_tokens = new(StringComparer.Ordinal)
    {
        ["alice-token"] = "Alice",
        ["bob-token"] = "Bob",
    };

    /// <summary>Validates the token and populates <see cref="CurrentUser"/> for the handler.</summary>
    public ValueTask<SliceFilterResult> InvokeAsync(SliceFilterContext context, SliceFilterDelegate next)
    {
        context.Headers.TryGetValue("X-User-Token", out var token);

        if (token is null || !s_tokens.TryGetValue(token, out var name))
        {
            return ValueTask.FromResult(
                SliceFilterResult.ShortCircuit(SliceResult.Unauthorized("Invalid or missing X-User-Token.")));
        }

        // Populate the scoped CurrentUser so the handler can inject it directly.
        var user = context.Services.GetRequiredService<CurrentUser>();
        user.Name = name;

        return next(context);
    }
}
