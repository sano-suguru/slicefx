using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SliceFx.TestHost.SliceApp.Services;

namespace SliceFx.TestHost.SliceApp.Features.Widgets;

[Feature("POST /widgets/{id}/promote", Summary = "Promote a widget")]
public static class PromoteWidget
{
    public sealed record Request([Required, MinLength(2)] string Tier);

    public sealed record Response(int Id, string Tier, string Audit, string Clock);

    public static Response Handle(
        int id,
        Request request,
        [FromServices] AuditRecorder audit,
        IWidgetStore store,
        [FromKeyedServices("promotion")] IClock clock)
    {
        audit.Record($"promote:{id}:{request.Tier}");
        _ = store;
        return new Response(id, request.Tier, audit.GetLatestEntry(), clock.Name);
    }
}
