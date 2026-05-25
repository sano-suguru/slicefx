using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Amazon.Lambda.APIGatewayEvents;

namespace Slice.Lambda.FunctionPerFeature;

/// <summary>
/// Creates API Gateway HTTP API v2 responses for generated function-per-feature handlers.
/// </summary>
public static partial class LambdaResponseFactory
{
    private static readonly Dictionary<string, string> s_jsonHeaders =
        new(StringComparer.Ordinal) { ["Content-Type"] = "application/json" };

    /// <summary>
    /// Creates a 200 OK response with a JSON response body.
    /// </summary>
    public static APIGatewayHttpApiV2ProxyResponse Ok<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
        => Json(200, value, jsonTypeInfo);

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
        return Json(400, problem, LambdaResponseJsonContext.Default.ProblemDto);
    }

    /// <summary>
    /// Creates a problem details response.
    /// </summary>
    public static APIGatewayHttpApiV2ProxyResponse Problem(int status, string title, string? detail = null)
    {
        var problem = new ProblemDto("about:blank", title, status, detail, Errors: null);
        return Json(status, problem, LambdaResponseJsonContext.Default.ProblemDto);
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
