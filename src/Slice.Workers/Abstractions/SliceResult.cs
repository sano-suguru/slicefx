using System.Text.Json;

namespace Slice.Workers;

public static class SliceResult
{
    private static readonly IReadOnlyDictionary<string, string> s_jsonHeaders =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "application/json" };
    private static readonly IReadOnlyDictionary<string, string> s_emptyHeaders =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly byte[] s_emptyBody = [];

    public static WorkerResponse Ok(object? value = null) => value is null ? new(200, s_emptyHeaders, s_emptyBody) : new(200, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(value));

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
        return new(400, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(problem));
    }

    public static WorkerResponse Problem(int status, string title, string? detail = null)
    {
        var problem = new ProblemDto("about:blank", title, status, detail, Errors: null);
        return new(status, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(problem));
    }

    public static WorkerResponse Json(int status, object? value)
    {
        return value is null
            ? new(status, s_emptyHeaders, s_emptyBody)
            : new(status, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(value));
    }

    public static WorkerResponse Bytes(int status, string contentType, byte[] bytes)
        => new(status, new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = contentType }, bytes);

    private sealed record ProblemDto(
        string Type,
        string Title,
        int Status,
        string? Detail,
        IReadOnlyDictionary<string, string[]>? Errors);
}
