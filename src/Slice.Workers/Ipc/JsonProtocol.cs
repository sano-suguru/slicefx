using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slice.Workers.Ipc;

internal static class JsonProtocol
{
    public static WorkerRequest? ParseRequest(string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize(json, IpcJsonContext.Default.RequestDto);
            return dto is null
                ? null
                : new WorkerRequest(
                dto.Method ?? "GET",
                dto.Path ?? "/",
                (IReadOnlyDictionary<string, string>?)dto.Headers ?? new Dictionary<string, string>(StringComparer.Ordinal),
                dto.Query,
                dto.Body is null ? null : Convert.FromBase64String(dto.Body));
        }
        catch
        {
            return null;
        }
    }

    public static string SerializeResponse(WorkerResponse response)
    {
        var headers = response.Headers is Dictionary<string, string> d
            ? d
            : new Dictionary<string, string>(response.Headers);
        var dto = new ResponseDto(
            response.Status,
            headers,
            response.Body.Length == 0 ? null : Convert.ToBase64String(response.Body));
        return JsonSerializer.Serialize(dto, IpcJsonContext.Default.ResponseDto);
    }

    internal sealed record RequestDto(
        string? Method,
        string? Path,
        Dictionary<string, string>? Headers,
        string? Query,
        string? Body);

    internal sealed record ResponseDto(
        int Status,
        Dictionary<string, string> Headers,
        string? Body);
}

[JsonSerializable(typeof(JsonProtocol.RequestDto))]
[JsonSerializable(typeof(JsonProtocol.ResponseDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class IpcJsonContext : JsonSerializerContext { }
