using System.Text;
using System.Text.Json;
using SliceFx.WasiSample.Features.Notes;

namespace SliceFx.WasiSample.Tests;

public sealed class NotesFeatureTests
{
    private static readonly IReadOnlyDictionary<string, string> JsonHeaders =
        new Dictionary<string, string> { ["Content-Type"] = "application/json" };

    private static WasiRequest Put(string id, string json) =>
        new("PUT", $"/notes/{id}", JsonHeaders, QueryString: null, Body: Encoding.UTF8.GetBytes(json));

    private static WasiRequest Get(string path) =>
        new("GET", path, new Dictionary<string, string>(), QueryString: null, Body: null);

    private static NoteView ReadNote(WasiResponse response) =>
        JsonSerializer.Deserialize(response.Body, WasiJsonContext.Default.NoteView)!;

    [Fact]
    public async Task Put_new_note_returns_201()
    {
        await using var app = SampleWasiApp.Create();
        var ct = TestContext.Current.CancellationToken;

        var response = await app.DispatchAsync(Put("a1", /*lang=json,strict*/ """{"title":"First","body":"hello"}"""), ct);

        Assert.Equal(201, response.Status);
        Assert.Contains(response.Headers, h => string.Equals(h.Key, "Location", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Put_existing_note_returns_200()
    {
        await using var app = SampleWasiApp.Create();
        var ct = TestContext.Current.CancellationToken;
        await app.DispatchAsync(Put("a1", /*lang=json,strict*/ """{"title":"First"}"""), ct);

        var response = await app.DispatchAsync(Put("a1", /*lang=json,strict*/ """{"title":"Updated"}"""), ct);

        Assert.Equal(200, response.Status);
        Assert.Equal("Updated", ReadNote(response).Title);
    }

    [Fact]
    public async Task Get_after_put_round_trips()
    {
        await using var app = SampleWasiApp.Create();
        var ct = TestContext.Current.CancellationToken;
        await app.DispatchAsync(Put("a1", /*lang=json,strict*/ """{"title":"First","body":"hello"}"""), ct);

        var response = await app.DispatchAsync(Get("/notes/a1"), ct);

        Assert.Equal(200, response.Status);
        var note = ReadNote(response);
        Assert.Equal("a1", note.Id);
        Assert.Equal("First", note.Title);
        Assert.Equal("hello", note.Body);
    }

    [Fact]
    public async Task Get_missing_note_returns_404()
    {
        await using var app = SampleWasiApp.Create();

        var response = await app.DispatchAsync(Get("/notes/nope"), TestContext.Current.CancellationToken);

        Assert.Equal(404, response.Status);
    }

    private static WasiRequest Delete(string id) =>
        new("DELETE", $"/notes/{id}", new Dictionary<string, string>(), QueryString: null, Body: null);

    [Fact]
    public async Task List_returns_all_notes()
    {
        await using var app = SampleWasiApp.Create();
        var ct = TestContext.Current.CancellationToken;
        await app.DispatchAsync(Put("a1", /*lang=json,strict*/ """{"title":"First"}"""), ct);
        await app.DispatchAsync(Put("a2", /*lang=json,strict*/ """{"title":"Second"}"""), ct);

        var response = await app.DispatchAsync(Get("/notes"), ct);

        Assert.Equal(200, response.Status);
        var list = JsonSerializer.Deserialize(response.Body, WasiJsonContext.Default.NoteListResponse)!;
        Assert.Equal(2, list.Notes.Count);
        Assert.Contains(list.Notes, n => n.Id == "a1");
        Assert.Contains(list.Notes, n => n.Id == "a2");
    }

    [Fact]
    public async Task Delete_existing_note_returns_204_then_get_404()
    {
        await using var app = SampleWasiApp.Create();
        var ct = TestContext.Current.CancellationToken;
        await app.DispatchAsync(Put("a1", /*lang=json,strict*/ """{"title":"First"}"""), ct);

        var deleteResponse = await app.DispatchAsync(Delete("a1"), ct);
        var getResponse = await app.DispatchAsync(Get("/notes/a1"), ct);

        Assert.Equal(204, deleteResponse.Status);
        Assert.Equal(404, getResponse.Status);
    }

    [Fact]
    public async Task Delete_missing_note_returns_404()
    {
        await using var app = SampleWasiApp.Create();

        var response = await app.DispatchAsync(Delete("nope"), TestContext.Current.CancellationToken);

        Assert.Equal(404, response.Status);
    }
}
