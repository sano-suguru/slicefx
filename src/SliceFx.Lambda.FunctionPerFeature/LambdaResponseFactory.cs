using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Amazon.Lambda.APIGatewayEvents;

namespace SliceFx.Lambda.FunctionPerFeature;

/// <summary>
/// Creates API Gateway HTTP API v2 responses for generated function-per-feature handlers.
/// </summary>
public static partial class LambdaResponseFactory
{
    private static readonly Dictionary<string, string> s_jsonHeaders =
        new(StringComparer.Ordinal) { ["Content-Type"] = "application/json" };
    private static readonly Dictionary<string, string> s_problemHeaders =
        new(StringComparer.Ordinal) { ["Content-Type"] = "application/problem+json" };

    /// <summary>
    /// Creates a 200 OK response with a JSON response body.
    /// </summary>
    public static APIGatewayHttpApiV2ProxyResponse Ok<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
        => Json(200, value, jsonTypeInfo);

    /// <summary>
    /// Creates a 201 Created response with a <c>Location</c> header and JSON body.
    /// </summary>
    public static APIGatewayHttpApiV2ProxyResponse Created<T>(string location, T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Location"] = location,
            ["Content-Type"] = "application/json",
        };
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = 201,
            Headers = headers,
            Body = value is null ? "null" : JsonSerializer.Serialize(value, jsonTypeInfo),
        };
    }

    /// <summary>
    /// Creates a 204 No Content response.
    /// </summary>
    public static APIGatewayHttpApiV2ProxyResponse NoContent()
        => new() { StatusCode = 204 };

    /// <summary>
    /// Creates a response with the specified status code and a JSON body.
    /// </summary>
    public static APIGatewayHttpApiV2ProxyResponse Json<T>(int status, T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = status,
            Headers = new Dictionary<string, string>(s_jsonHeaders, StringComparer.Ordinal),
            Body = value is null ? "null" : JsonSerializer.Serialize(value, jsonTypeInfo),
        };
    }

    /// <summary>
    /// Creates a validation problem response.
    /// </summary>
    public static APIGatewayHttpApiV2ProxyResponse ValidationProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = new ProblemDto(
            "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            "One or more validation errors occurred.",
            400,
            Detail: null,
            Errors: errors);
        return ProblemJson(400, problem);
    }

    /// <summary>
    /// Creates a problem details response.
    /// </summary>
    public static APIGatewayHttpApiV2ProxyResponse Problem(int status, string title, string? detail = null)
    {
        var problem = new ProblemDto("about:blank", title, status, detail, Errors: null);
        return ProblemJson(status, problem);
    }

    private static APIGatewayHttpApiV2ProxyResponse ProblemJson(int status, ProblemDto problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = status,
            Headers = new Dictionary<string, string>(s_problemHeaders, StringComparer.Ordinal),
            Body = JsonSerializer.Serialize(problem, LambdaResponseJsonContext.Default.ProblemDto),
        };
    }

    private sealed partial record ProblemDto(
        string Type,
        string Title,
        int Status,
        string? Detail,
        IReadOnlyDictionary<string, string[]>? Errors);

    [JsonSerializable(typeof(ProblemDto))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    private sealed partial class LambdaResponseJsonContext : JsonSerializerContext;
}
