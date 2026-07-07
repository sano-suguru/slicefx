using System.ComponentModel.DataAnnotations;
using SliceFx.Wasi.KeyValue;

namespace SliceFx.WasiSample.Features.Notes;

/// <summary>Creates or updates a note at the client-supplied id (idempotent upsert).</summary>
[Feature("PUT /notes/{id}", Summary = "Create or update a note")]
public static class PutNote
{
    /// <summary>Request body for creating or updating a note.</summary>
    /// <param name="Title">Note title (required, 1-200 chars).</param>
    /// <param name="Body">Optional note body.</param>
    public record Request([Required, StringLength(200, MinimumLength = 1)] string Title, string? Body);

    /// <summary>Upserts the note into the key-value store under <c>note:{id}</c>.</summary>
    public static async Task<SliceResult<NoteView>> Handle(
        string id, Request req, IKeyValueStore kv, TimeProvider clock, CancellationToken ct)
    {
        var key = $"note:{id}";
        var existed = await kv.ExistsAsync(key, ct);
        var view = new NoteView(id, req.Title, req.Body, clock.GetUtcNow());
        await kv.SetJsonAsync(key, view, WasiJsonContext.Default.NoteView, ct);
        return existed
            ? SliceResult<NoteView>.Ok(view)
            : SliceResult<NoteView>.Created(view, $"/notes/{id}");
    }
}
