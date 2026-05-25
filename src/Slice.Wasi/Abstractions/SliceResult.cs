using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Slice.Wasi;

/// <summary>
/// Factory methods for common <see cref="WasiResponse"/> values used by Slice WASI features.
/// </summary>
public static partial class SliceResult
{
    private static readonly IReadOnlyDictionary<string, string> s_jsonHeaders =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "application/json" };
    private static readonly IReadOnlyDictionary<string, string> s_emptyHeaders =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly byte[] s_emptyBody = [];

    /// <summary>
    /// Creates a 200 OK response with an empty body.
    /// </summary>
    /// <returns>A 200 OK WASI response.</returns>
    public static WasiResponse Ok() => new(200, s_emptyHeaders, s_emptyBody);

    /// <summary>
    /// Creates a 200 OK response with a JSON response body.
    /// </summary>
    /// <param name="value">The value to serialize as JSON. <c>null</c> is serialized as JSON <c>null</c>.</param>
    /// <returns>A 200 OK WASI response.</returns>
    /// <remarks>This overload uses reflection-based JSON serialization. Use <see cref="Ok{T}(T, JsonTypeInfo{T})"/> for NativeAOT and trimming.</remarks>
    [RequiresDynamicCode("Use Ok<T>(T, JsonTypeInfo<T>) for NativeAOT-compatible WASI responses.")]
    [RequiresUnreferencedCode("Use Ok<T>(T, JsonTypeInfo<T>) for trim-compatible WASI responses.")]
    public static WasiResponse Ok(object? value)
        => new(200, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(value));

    /// <summary>
    /// Creates a 200 OK response with a JSON response body using source-generated metadata.
    /// </summary>
    /// <typeparam name="T">The response body type.</typeparam>
    /// <param name="value">The value to serialize as JSON. <c>null</c> is serialized as JSON <c>null</c>.</param>
    /// <param name="jsonTypeInfo">The JSON metadata to use when serializing <paramref name="value"/>.</param>
    /// <returns>A 200 OK WASI response.</returns>
    public static WasiResponse Ok<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
        => new(200, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo));

    /// <summary>
    /// Creates a 201 Created response with a <c>Location</c> header and empty body.
    /// </summary>
    /// <param name="location">The resource location to write to the <c>Location</c> header.</param>
    /// <returns>A 201 Created WASI response.</returns>
    public static WasiResponse Created(string location)
        => new(201, new Dictionary<string, string>(StringComparer.Ordinal) { ["Location"] = location }, s_emptyBody);

    /// <summary>
    /// Creates a 201 Created response with a <c>Location</c> header and JSON response body.
    /// </summary>
    /// <param name="location">The resource location to write to the <c>Location</c> header.</param>
    /// <param name="value">The value to serialize as JSON. <c>null</c> is serialized as JSON <c>null</c>.</param>
    /// <returns>A 201 Created WASI response.</returns>
    /// <remarks>This overload uses reflection-based JSON serialization. Use <see cref="Created{T}(string, T, JsonTypeInfo{T})"/> for NativeAOT and trimming.</remarks>
    [RequiresDynamicCode("Use Created<T>(string, T, JsonTypeInfo<T>) for NativeAOT-compatible WASI responses.")]
    [RequiresUnreferencedCode("Use Created<T>(string, T, JsonTypeInfo<T>) for trim-compatible WASI responses.")]
    public static WasiResponse Created(string location, object? value)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Location"] = location,
            ["Content-Type"] = "application/json",
        };
        return new(201, headers, JsonSerializer.SerializeToUtf8Bytes(value));
    }

    /// <summary>
    /// Creates a 201 Created response with a <c>Location</c> header and JSON response body using source-generated metadata.
    /// </summary>
    /// <typeparam name="T">The response body type.</typeparam>
    /// <param name="location">The resource location to write to the <c>Location</c> header.</param>
    /// <param name="value">The value to serialize as JSON. <c>null</c> is serialized as JSON <c>null</c>.</param>
    /// <param name="jsonTypeInfo">The JSON metadata to use when serializing <paramref name="value"/>.</param>
    /// <returns>A 201 Created WASI response.</returns>
    public static WasiResponse Created<T>(string location, T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Location"] = location,
            ["Content-Type"] = "application/json",
        };
        return new(201, headers, JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo));
    }

    /// <summary>
    /// Creates a 204 No Content response.
    /// </summary>
    /// <returns>A 204 No Content WASI response with an empty body.</returns>
    public static WasiResponse NoContent() => new(204, s_emptyHeaders, s_emptyBody);

    /// <summary>
    /// Creates a 404 Not Found response.
    /// </summary>
    /// <returns>A 404 Not Found WASI response with an empty body.</returns>
    public static WasiResponse NotFound() => new(404, s_emptyHeaders, s_emptyBody);

    /// <summary>
    /// Creates a 401 Unauthorized response.
    /// </summary>
    /// <returns>A 401 Unauthorized WASI response with an empty body.</returns>
    public static WasiResponse Unauthorized() => new(401, s_emptyHeaders, s_emptyBody);

    /// <summary>
    /// Creates a 400 validation problem response from field-keyed validation errors.
    /// </summary>
    /// <param name="errors">A dictionary of field names to one or more validation messages.</param>
    /// <returns>A JSON Problem Details WASI response with validation errors.</returns>
    public static WasiResponse ValidationProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = new ProblemDto(
            "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            "One or more validation errors occurred.",
            400,
            Detail: null,
            Errors: errors);
        return new(400, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(problem, SliceResultJsonContext.Default.ProblemDto));
    }

    /// <summary>
    /// Creates a JSON Problem Details response.
    /// </summary>
    /// <param name="status">The HTTP status code for the problem response.</param>
    /// <param name="title">The short, human-readable problem title.</param>
    /// <param name="detail">Optional details about the specific problem occurrence.</param>
    /// <returns>A JSON Problem Details WASI response.</returns>
    public static WasiResponse Problem(int status, string title, string? detail = null)
    {
        var problem = new ProblemDto("about:blank", title, status, detail, Errors: null);
        return new(status, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(problem, SliceResultJsonContext.Default.ProblemDto));
    }

    /// <summary>
    /// Creates a response with the specified status code and a JSON response body.
    /// </summary>
    /// <param name="status">The HTTP status code for the response.</param>
    /// <param name="value">The value to serialize as JSON. <c>null</c> is serialized as JSON <c>null</c>.</param>
    /// <returns>A WASI response with the specified status code.</returns>
    /// <remarks>This overload uses reflection-based JSON serialization. Use <see cref="Json{T}(int, T, JsonTypeInfo{T})"/> for NativeAOT and trimming.</remarks>
    [RequiresDynamicCode("Use Json<T>(int, T, JsonTypeInfo<T>) for NativeAOT-compatible WASI responses.")]
    [RequiresUnreferencedCode("Use Json<T>(int, T, JsonTypeInfo<T>) for trim-compatible WASI responses.")]
    public static WasiResponse Json(int status, object? value)
        => new(status, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(value));

    /// <summary>
    /// Creates a response with the specified status code and a JSON response body using source-generated metadata.
    /// </summary>
    /// <typeparam name="T">The response body type.</typeparam>
    /// <param name="status">The HTTP status code for the response.</param>
    /// <param name="value">The value to serialize as JSON. <c>null</c> is serialized as JSON <c>null</c>.</param>
    /// <param name="jsonTypeInfo">The JSON metadata to use when serializing <paramref name="value"/>.</param>
    /// <returns>A WASI response with the specified status code.</returns>
    public static WasiResponse Json<T>(int status, T value, JsonTypeInfo<T> jsonTypeInfo)
        => new(status, s_jsonHeaders, JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo));

    /// <summary>
    /// Creates a response from pre-serialized bytes.
    /// </summary>
    /// <param name="status">The HTTP status code for the response.</param>
    /// <param name="contentType">The value to write to the <c>Content-Type</c> header.</param>
    /// <param name="bytes">The response body bytes.</param>
    /// <returns>A WASI response containing the provided bytes.</returns>
    public static WasiResponse Bytes(int status, string contentType, byte[] bytes)
        => new(status, new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = contentType }, bytes);

    private sealed partial record ProblemDto(
        string Type,
        string Title,
        int Status,
        string? Detail,
        IReadOnlyDictionary<string, string[]>? Errors);

    [JsonSerializable(typeof(ProblemDto))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    private sealed partial class SliceResultJsonContext : JsonSerializerContext { }
}
