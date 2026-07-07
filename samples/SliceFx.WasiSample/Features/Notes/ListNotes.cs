using SliceFx.Wasi.KeyValue;

namespace SliceFx.WasiSample.Features.Notes;

/// <summary>Lists all stored notes via a prefix scan of the key-value store.</summary>
[Feature("GET /notes", Summary = "List all notes")]
public static class ListNotes
{
    /// <summary>Wraps the note collection so the WASI JSON root is a named object.</summary>
    /// <param name="Notes">All stored notes.</param>
    public record Response(IReadOnlyList<NoteView> Notes);

    /// <summary>Scans keys under the <c>note:</c> prefix and returns each stored note.</summary>
    public static async Task<SliceResult<Response>> Handle(IKeyValueStore kv, CancellationToken ct)
    {
        var keys = await kv.ListKeysAsync(ct);
        var notes = new List<NoteView>();
        foreach (var key in keys)
        {
            if (!key.StartsWith("note:", StringComparison.Ordinal))
            {
                continue;
            }

            var view = await kv.GetJsonAsync(key, WasiJsonContext.Default.NoteView, ct);
            if (view is not null)
            {
                notes.Add(view);
            }
        }

        return SliceResult<Response>.Ok(new Response(notes));
    }
}
