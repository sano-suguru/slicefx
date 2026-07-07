using SliceFx.Wasi.KeyValue;

namespace SliceFx.WasiSample.Features.Notes;

/// <summary>Reads a single note by id from the key-value store.</summary>
[Feature("GET /notes/{id}", Summary = "Get a note by id")]
public static class GetNote
{
    /// <summary>Returns the stored note, or 404 when the key is absent.</summary>
    public static async Task<SliceResult<NoteView>> Handle(string id, IKeyValueStore kv, CancellationToken ct)
    {
        var view = await kv.GetJsonAsync($"note:{id}", WasiJsonContext.Default.NoteView, ct);
        return view is null
            ? SliceResult<NoteView>.NotFound($"Note '{id}' not found.")
            : SliceResult<NoteView>.Ok(view);
    }
}
