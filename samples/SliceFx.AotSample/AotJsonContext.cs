using System.Text.Json.Serialization;
using SliceFx.AotSample.Features.Health;
using SliceFx.AotSample.Features.Todos;

namespace SliceFx.AotSample;

[SliceJsonContext(SliceJsonTarget.AspNet)]
[JsonSerializable(typeof(GetHealth.Response))]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(List<Todo>))]
[JsonSerializable(typeof(CreateTodo.Request))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AotJsonContext : JsonSerializerContext { }
