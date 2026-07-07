using SliceFx.Wasi.KeyValue;

namespace SliceFx.WasiSample.Features.Notes;

/// <summary>Deletes a note by id. Returns 204 on success, 404 when absent.</summary>
[Feature("DELETE /notes/{id}", Summary = "Delete a note by id")]
public static class DeleteNote
{
    /// <summary>Removes <c>note:{id}</c> from the store; non-generic result (no body on success).</summary>
    public static async Task<SliceResult> Handle(string id, IKeyValueStore kv, CancellationToken ct)
    {
        var key = $"note:{id}";
        if (!await kv.ExistsAsync(key, ct))
        {
            return SliceResult.NotFound($"Note '{id}' not found.");
        }

        await kv.DeleteAsync(key, ct);
        return SliceResult.NoContent();
    }
}
