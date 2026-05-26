using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SliceFx.Sample.Services;

namespace SliceFx.Sample.Features.Users;

/// <summary>
/// Demonstrates a POST handler that uses concrete and keyed DI services alongside a body contract.
///
/// Under the new portable-dispatch convention, the source generator infers the body contract from a
/// single request-like DTO on POST/PUT/PATCH handlers. Concrete DI services must be annotated with
/// [FromServices] so they are not misidentified as a body parameter. Keyed DI registrations require
/// [FromKeyedServices(key)].
/// </summary>
[Feature("POST /users/{id:guid}/promote", Summary = "Promote a user to a tier (DI annotation demo)")]
public static class PromoteUser
{
    /// <summary>Request body for the promote operation.</summary>
    /// <param name="Tier">Target tier name (e.g. "gold").</param>
    public record Request([Required, MinLength(1)] string Tier);

    /// <summary>Response returned after a successful promotion.</summary>
    /// <param name="Id">User identifier.</param>
    /// <param name="Tier">Assigned tier.</param>
    /// <param name="PromotedAt">UTC timestamp of the promotion.</param>
    public record Response(Guid Id, string Tier, DateTime PromotedAt);

    /// <param name="id">User identifier from the route.</param>
    /// <param name="req">Validated request body (inferred body contract — single DTO on POST).</param>
    /// <param name="audit">Concrete service: requires [FromServices] so the generator does not infer it as a body parameter.</param>
    /// <param name="clock">Keyed interface: registered under "promotion" key; requires [FromKeyedServices].</param>
    /// <param name="ct">Request cancellation token.</param>
    public static async Task<Response> Handle(
        Guid id,
        Request req,
        [FromServices] AuditLog audit,
        [FromKeyedServices("promotion")] IClock clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await audit.RecordAsync($"promote user {id} -> {req.Tier} @ {now:o}", ct).ConfigureAwait(false);
        return new Response(id, req.Tier, now.UtcDateTime);
    }
}
