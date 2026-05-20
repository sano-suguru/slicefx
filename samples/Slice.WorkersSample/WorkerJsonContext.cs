using System.Text.Json.Serialization;

namespace Slice.WorkersSample;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Features.Echo.PostEcho.Request), TypeInfoPropertyName = "PostEchoRequest")]
[JsonSerializable(typeof(Features.Echo.PostEcho.Response), TypeInfoPropertyName = "PostEchoResponse")]
[JsonSerializable(typeof(Features.Health.GetHealth.Response), TypeInfoPropertyName = "GetHealthResponse")]
[JsonSerializable(typeof(Dictionary<string, List<int>>), TypeInfoPropertyName = "NestedGenericResponse")]
[JsonSerializable(typeof(Features.Validation.PostArrayMinLength.Request), TypeInfoPropertyName = "ArrayMinLengthRequest")]
[JsonSerializable(typeof(Features.Validation.PostArrayMinLength.Response), TypeInfoPropertyName = "ArrayMinLengthResponse")]
[JsonSerializable(typeof(Features.Validation.PostValidationFallback.Request), TypeInfoPropertyName = "ValidationFallbackRequest")]
[JsonSerializable(typeof(Features.Validation.PostValidationFallback.Response), TypeInfoPropertyName = "ValidationFallbackResponse")]
public sealed partial class WorkerJsonContext : JsonSerializerContext
{
}
