namespace SliceFx.WasiSample.Features.Notes;

/// <summary>
/// A stored note. Serialized into the key-value store and returned by the Notes features,
/// so the same shape round-trips through <c>IKeyValueStore</c> and the HTTP response.
/// </summary>
/// <param name="Id">Client-supplied note identifier (the <c>{id}</c> route segment).</param>
/// <param name="Title">Note title.</param>
/// <param name="Body">Optional note body.</param>
/// <param name="UpdatedAt">UTC timestamp of the last write, from the injected clock.</param>
public sealed record NoteView(string Id, string Title, string? Body, DateTimeOffset UpdatedAt);
