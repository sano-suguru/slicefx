using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Slice.Workers;

public static partial class SliceResult
{
    private static readonly IReadOnlyDictionary<string, string> s_jsonHeaders =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "application/json" };
    private static readonly IReadOnlyDictionary<string, string> s_emptyHeaders =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly byte[] s_emptyBody = [];

    [RequiresDynamicCode("Use Ok<T>(T, JsonTypeInfo<T>) for NativeAOT-compatible Workers responses.")]
    [RequiresUnreferencedCode("Use Ok<T>(T, JsonTypeInfo<T>) for trim-compatible Workers responses.")]
    public static WorkerResponse Ok(object? value = null) => value is null ? new(200, s_emptyHeaders, s_emptyBody) : new(200, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(value));

    public static WorkerResponse Ok<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
        => value is null ? new(200, s_emptyHeaders, s_emptyBody) : new(200, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo));

    [RequiresDynamicCode("Use Created<T>(string, T, JsonTypeInfo<T>) for NativeAOT-compatible Workers responses.")]
    [RequiresUnreferencedCode("Use Created<T>(string, T, JsonTypeInfo<T>) for trim-compatible Workers responses.")]
    public static WorkerResponse Created(string location, object? value = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Location"] = location };
        if (value is not null)
        {
            headers["Content-Type"] = "application/json";
            return new(201, headers, JsonSerializer.SerializeToUtf8Bytes(value));
        }
        return new(201, headers, s_emptyBody);
    }

    public static WorkerResponse Created<T>(string location, T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Location"] = location };
        if (value is null)
        {
            return new(201, headers, s_emptyBody);
        }

        headers["Content-Type"] = "application/json";
        return new(201, headers, JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo));
    }

    public static WorkerResponse NoContent() => new(204, s_emptyHeaders, s_emptyBody);
    public static WorkerResponse NotFound() => new(404, s_emptyHeaders, s_emptyBody);
    public static WorkerResponse Unauthorized() => new(401, s_emptyHeaders, s_emptyBody);

    public static WorkerResponse ValidationProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = new ProblemDto(
            "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            "One or more validation errors occurred.",
            400,
            Detail: null,
            Errors: errors);
        return new(400, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(problem, SliceResultJsonContext.Default.ProblemDto));
    }

    public static WorkerResponse Problem(int status, string title, string? detail = null)
    {
        var problem = new ProblemDto("about:blank", title, status, detail, Errors: null);
        return new(status, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(problem, SliceResultJsonContext.Default.ProblemDto));
    }

    [RequiresDynamicCode("Use Json<T>(int, T, JsonTypeInfo<T>) for NativeAOT-compatible Workers responses.")]
    [RequiresUnreferencedCode("Use Json<T>(int, T, JsonTypeInfo<T>) for trim-compatible Workers responses.")]
    public static WorkerResponse Json(int status, object? value)
    {
        return value is null
            ? new(status, s_emptyHeaders, s_emptyBody)
            : new(status, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(value));
    }

    public static WorkerResponse Json<T>(int status, T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        return value is null
            ? new(status, s_emptyHeaders, s_emptyBody)
            : new(status, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo));
    }

    public static WorkerResponse Bytes(int status, string contentType, byte[] bytes)
        => new(status, new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = contentType }, bytes);

    private sealed partial record ProblemDto(
        string Type,
        string Title,
        int Status,
        string? Detail,
        IReadOnlyDictionary<string, string[]>? Errors);

    [JsonSerializable(typeof(ProblemDto))]
    private sealed partial class SliceResultJsonContext : JsonSerializerContext { }
}
