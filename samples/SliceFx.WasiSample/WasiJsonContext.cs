using System.Text.Json.Serialization;

namespace SliceFx.WasiSample;

/// <summary>
/// Source-generated JSON metadata used by the WASI sample routes.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Features.Echo.PostEcho.Request), TypeInfoPropertyName = "PostEchoRequest")]
[JsonSerializable(typeof(Features.Echo.PostEcho.Response), TypeInfoPropertyName = "PostEchoResponse")]
[JsonSerializable(typeof(Features.Health.GetHealth.Response), TypeInfoPropertyName = "GetHealthResponse")]
[JsonSerializable(typeof(Dictionary<string, List<int>>), TypeInfoPropertyName = "NestedGenericResponse")]
[JsonSerializable(typeof(Features.Validation.PostArrayMinLength.Request), TypeInfoPropertyName = "ArrayMinLengthRequest")]
[JsonSerializable(typeof(Features.Validation.PostArrayMinLength.Response), TypeInfoPropertyName = "ArrayMinLengthResponse")]
[JsonSerializable(typeof(Features.Validation.PostValidationFallback.Request), TypeInfoPropertyName = "ValidationFallbackRequest")]
[JsonSerializable(typeof(Features.Validation.PostValidationFallback.Response), TypeInfoPropertyName = "ValidationFallbackResponse")]
[JsonSerializable(typeof(Features.Diagnostics.GetSecuredDiagnostics.Response), TypeInfoPropertyName = "GetSecuredDiagnosticsResponse")]
[JsonSerializable(typeof(Features.Notes.NoteView), TypeInfoPropertyName = "NoteView")]
[JsonSerializable(typeof(Features.Notes.PutNote.Request), TypeInfoPropertyName = "PutNoteRequest")]
[JsonSerializable(typeof(Features.Notes.ListNotes.Response), TypeInfoPropertyName = "NoteListResponse")]
[JsonSerializable(typeof(Features.Fetch.PostFetch.Request), TypeInfoPropertyName = "PostFetchRequest")]
[JsonSerializable(typeof(Features.Fetch.PostFetch.Response), TypeInfoPropertyName = "PostFetchResponse")]
[SliceJsonContext(SliceJsonTarget.Wasi)]
public sealed partial class WasiJsonContext : JsonSerializerContext
{
}
