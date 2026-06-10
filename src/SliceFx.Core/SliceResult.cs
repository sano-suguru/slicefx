// CA1000: Static factory methods on a generic type are intentional for the fluent API pattern
// SliceResult<T>.Ok(value), SliceResult<T>.NotFound(), etc.
#pragma warning disable CA1000
// CA1819: Arrays returned from properties are intentional; Body is raw bytes already allocated by
// factory methods. Callers should not mutate the array; this is documented on the Body property.
#pragma warning disable CA1819

using System.Text;

namespace SliceFx;

/// <summary>
/// Discriminates the kind of result carried by a non-generic <see cref="SliceResult"/>.
/// </summary>
public enum SliceResultKind
{
    /// <summary>Status code only (no special body or redirect). Covers Ok, NoContent, Created, and Problem responses.</summary>
    StatusOnly,

    /// <summary>HTTP redirect (301 Permanent or 302 Temporary). The <see cref="SliceResult.Location"/> property contains the target URL.</summary>
    Redirect,

    /// <summary>Raw body response with an explicit <c>Content-Type</c>. Used for Html, Text, Content, and Bytes responses.</summary>
    RawBody,
}

/// <summary>
/// A host-neutral typed result for Slice features that combines a typed response body
/// with a variable HTTP status code.
/// </summary>
/// <typeparam name="T">The type of the response body when the operation succeeds with a body.</typeparam>
/// <remarks>
/// <para>
/// <see cref="SliceResult{T}"/> sits in <c>SliceFx.Core</c> (namespace <c>SliceFx</c>) and carries
/// only data — no serialization logic. Each host adapter (e.g., <c>SliceFx.Wasi</c>) provides an
/// extension method that converts this value to the host's native response type.
/// </para>
/// <para>
/// Use this type when a feature needs to express either a typed success body (200/201) or an error
/// path (404, 401, 400, etc.) from the same <c>Handle</c> method. The source generator detects
/// <see cref="SliceResult{T}"/> return types and emits the host translation automatically — no
/// <c>JsonTypeInfo</c> argument is needed in <c>Handle</c>.
/// </para>
/// <code>
/// // Example: typed result with an error path
/// [Feature("GET /items/{id}")]
/// public static class GetItem
/// {
///     public static async Task&lt;SliceResult&lt;GetItemResponse&gt;&gt; Handle(
///         string id, IStore store, CancellationToken ct)
///     {
///         var item = await store.GetAsync(id, ct);
///         if (item is null) return SliceResult&lt;GetItemResponse&gt;.NotFound($"Item '{id}' not found.");
///         return SliceResult&lt;GetItemResponse&gt;.Ok(new GetItemResponse(item));
///     }
/// }
/// </code>
/// <para>
/// For features whose success path has no body (204 No Content etc.), use the non-generic
/// <see cref="SliceResult"/> struct instead.
/// </para>
/// </remarks>
public readonly struct SliceResult<T>
{
    // Single private constructor: all fields are always set here so every factory produces a
    // fully-initialized struct. This avoids CS8618/CS8601 under TreatWarningsAsErrors + Nullable enable.
    private SliceResult(
        int status,
        bool isSuccess,
        bool hasBody,
        T? value,
        string? location,
        string? problemTitle,
        string? problemDetail)
    {
        Status = status;
        IsSuccess = isSuccess;
        HasBody = hasBody;
        Value = value;
        Location = location;
        ProblemTitle = problemTitle;
        ProblemDetail = problemDetail;
    }

    /// <summary>The HTTP status code for this result.</summary>
    public int Status { get; }

    /// <summary><c>true</c> when the operation succeeded; <c>false</c> for error results.</summary>
    public bool IsSuccess { get; }

    /// <summary><c>true</c> when the result carries a serializable response body.</summary>
    public bool HasBody { get; }

    /// <summary>
    /// The typed response body. Meaningful only when <see cref="HasBody"/> is <c>true</c>.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// The <c>Location</c> header value for 201 Created results; <c>null</c> otherwise.
    /// When non-null, the WASI translation always emits status 201.
    /// </summary>
    public string? Location { get; }

    /// <summary>The problem title for error results; <c>null</c> for success results.</summary>
    public string? ProblemTitle { get; }

    /// <summary>Optional problem detail for error results; <c>null</c> when not provided.</summary>
    public string? ProblemDetail { get; }

    /// <summary>Creates a 200 OK result with a typed response body.</summary>
    /// <param name="value">The response body value.</param>
    public static SliceResult<T> Ok(T value) =>
        new(200, isSuccess: true, hasBody: true, value, location: null, problemTitle: null, problemDetail: null);

    /// <summary>
    /// Creates a 201 Created result with a typed response body and a <c>Location</c> header.
    /// </summary>
    /// <param name="value">The response body value.</param>
    /// <param name="location">The resource location for the <c>Location</c> header.</param>
    /// <remarks>A non-null <see cref="Location"/> always implies status 201 in the WASI translation.</remarks>
    public static SliceResult<T> Created(T value, string location) =>
        new(201, isSuccess: true, hasBody: true, value, location, problemTitle: null, problemDetail: null);

    /// <summary>Creates a 204 No Content success result (no body).</summary>
    public static SliceResult<T> NoContent() =>
        new(204, isSuccess: true, hasBody: false, value: default, location: null, problemTitle: null, problemDetail: null);

    /// <summary>Creates a 404 Not Found error result (Problem Details, <c>application/problem+json</c>).</summary>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static SliceResult<T> NotFound(string? detail = null) =>
        new(404, isSuccess: false, hasBody: false, value: default, location: null, problemTitle: "Not Found", detail);

    /// <summary>Creates a 401 Unauthorized error result (Problem Details, <c>application/problem+json</c>).</summary>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static SliceResult<T> Unauthorized(string? detail = null) =>
        new(401, isSuccess: false, hasBody: false, value: default, location: null, problemTitle: "Unauthorized", detail);

    /// <summary>Creates a 400 Bad Request error result (Problem Details, <c>application/problem+json</c>).</summary>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static SliceResult<T> BadRequest(string? detail = null) =>
        new(400, isSuccess: false, hasBody: false, value: default, location: null, problemTitle: "Bad Request", detail);

    /// <summary>
    /// Creates an error result with an explicit status code, title, and optional detail (Problem Details).
    /// </summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="title">The short, human-readable problem title.</param>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static SliceResult<T> Problem(int status, string title, string? detail = null) =>
        new(status, isSuccess: false, hasBody: false, value: default, location: null, problemTitle: title, detail);
}

/// <summary>
/// A host-neutral status-only result for Slice features whose success path carries no response body
/// (for example, 204 No Content, or mutations that respond with only a status code).
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="SliceResult"/> when the success path is a 204 (no body) and failures are expressed
/// as Problem Details. The source generator emits a void (<c>Task</c>) client method for routes
/// returning <see cref="SliceResult"/>, so the generated client does not attempt to deserialize an
/// empty 204 body.
/// </para>
/// <para>
/// For features that return a typed body on success, use <see cref="SliceResult{T}"/> instead.
/// </para>
/// </remarks>
public readonly struct SliceResult
{
    // Single private constructor: all fields are always set here.
    private SliceResult(
        int status,
        bool isSuccess,
        SliceResultKind kind,
        string? location,
        string? contentType,
        byte[]? body,
        string? problemTitle,
        string? problemDetail)
    {
        Status = status;
        IsSuccess = isSuccess;
        Kind = kind;
        Location = location;
        ContentType = contentType;
        Body = body;
        ProblemTitle = problemTitle;
        ProblemDetail = problemDetail;
    }

    /// <summary>The HTTP status code for this result.</summary>
    public int Status { get; }

    /// <summary><c>true</c> when the operation succeeded; <c>false</c> for error results.</summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Indicates what kind of result this is: <see cref="SliceResultKind.StatusOnly"/>,
    /// <see cref="SliceResultKind.Redirect"/>, or <see cref="SliceResultKind.RawBody"/>.
    /// </summary>
    public SliceResultKind Kind { get; }

    /// <summary>
    /// The <c>Location</c> header value for 201 Created and redirect results; <c>null</c> otherwise.
    /// </summary>
    public string? Location { get; }

    /// <summary>
    /// The <c>Content-Type</c> header value for <see cref="SliceResultKind.RawBody"/> results;
    /// <c>null</c> for other result kinds.
    /// </summary>
    public string? ContentType { get; }

    /// <summary>
    /// The raw response body bytes for <see cref="SliceResultKind.RawBody"/> results;
    /// <c>null</c> for other result kinds.
    /// Do not mutate the returned array — it is owned by the result.
    /// </summary>
    public byte[]? Body { get; }

    /// <summary>The problem title for error results; <c>null</c> for success results.</summary>
    public string? ProblemTitle { get; }

    /// <summary>Optional problem detail for error results; <c>null</c> when not provided.</summary>
    public string? ProblemDetail { get; }

    /// <summary>Creates a 200 OK result (no body).</summary>
    public static SliceResult Ok() =>
        new(200, isSuccess: true, SliceResultKind.StatusOnly, location: null, contentType: null, body: null, problemTitle: null, problemDetail: null);

    /// <summary>Creates a 204 No Content result.</summary>
    public static SliceResult NoContent() =>
        new(204, isSuccess: true, SliceResultKind.StatusOnly, location: null, contentType: null, body: null, problemTitle: null, problemDetail: null);

    /// <summary>
    /// Creates a 201 Created result with a <c>Location</c> header (no body).
    /// </summary>
    /// <param name="location">The resource location for the <c>Location</c> header.</param>
    public static SliceResult Created(string location) =>
        new(201, isSuccess: true, SliceResultKind.StatusOnly, location, contentType: null, body: null, problemTitle: null, problemDetail: null);

    /// <summary>Creates a 302 Found (temporary) redirect, or 301 Moved Permanently when <paramref name="permanent"/> is <c>true</c>.</summary>
    /// <param name="location">The redirect target URL written to the <c>Location</c> header.</param>
    /// <param name="permanent">
    /// When <c>true</c>, emits 301 Moved Permanently; otherwise 302 Found.
    /// </param>
    public static SliceResult Redirect(string location, bool permanent = false) =>
        new(permanent ? 301 : 302, isSuccess: true, SliceResultKind.Redirect, location, contentType: null, body: null, problemTitle: null, problemDetail: null);

    /// <summary>
    /// Creates a response with an explicit body and <c>Content-Type</c>, using the given status code.
    /// The <paramref name="body"/> string is UTF-8 encoded; use <see cref="Bytes"/> for pre-encoded content.
    /// </summary>
    /// <param name="body">The response body text to encode as UTF-8.</param>
    /// <param name="contentType">The value to write to the <c>Content-Type</c> header.</param>
    /// <param name="status">The HTTP status code (default 200).</param>
    public static SliceResult Content(string body, string contentType, int status = 200) =>
        new(status, isSuccess: true, SliceResultKind.RawBody, location: null, contentType, Encoding.UTF8.GetBytes(body), problemTitle: null, problemDetail: null);

    /// <summary>
    /// Creates a 200 OK <c>text/html; charset=utf-8</c> response.
    /// Equivalent to <c>Content(html, "text/html; charset=utf-8")</c>.
    /// </summary>
    /// <param name="html">The HTML body text.</param>
    public static SliceResult Html(string html) =>
        Content(html, "text/html; charset=utf-8");

    /// <summary>
    /// Creates a 200 OK <c>text/plain; charset=utf-8</c> response.
    /// Equivalent to <c>Content(text, "text/plain; charset=utf-8")</c>.
    /// </summary>
    /// <param name="text">The plain-text body.</param>
    public static SliceResult Text(string text) =>
        Content(text, "text/plain; charset=utf-8");

    /// <summary>
    /// Creates a response from pre-encoded bytes with an explicit <c>Content-Type</c> and status.
    /// Use this when the caller has already encoded the body (e.g., binary formats).
    /// Do not mutate <paramref name="body"/> after passing it here.
    /// </summary>
    /// <param name="body">The raw response body bytes.</param>
    /// <param name="contentType">The value to write to the <c>Content-Type</c> header.</param>
    /// <param name="status">The HTTP status code (default 200).</param>
    public static SliceResult Bytes(byte[] body, string contentType, int status = 200) =>
        new(status, isSuccess: true, SliceResultKind.RawBody, location: null, contentType, body, problemTitle: null, problemDetail: null);

    /// <summary>Creates a 404 Not Found error result (Problem Details, <c>application/problem+json</c>).</summary>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static SliceResult NotFound(string? detail = null) =>
        new(404, isSuccess: false, SliceResultKind.StatusOnly, location: null, contentType: null, body: null, problemTitle: "Not Found", detail);

    /// <summary>Creates a 401 Unauthorized error result (Problem Details, <c>application/problem+json</c>).</summary>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static SliceResult Unauthorized(string? detail = null) =>
        new(401, isSuccess: false, SliceResultKind.StatusOnly, location: null, contentType: null, body: null, problemTitle: "Unauthorized", detail);

    /// <summary>Creates a 400 Bad Request error result (Problem Details, <c>application/problem+json</c>).</summary>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static SliceResult BadRequest(string? detail = null) =>
        new(400, isSuccess: false, SliceResultKind.StatusOnly, location: null, contentType: null, body: null, problemTitle: "Bad Request", detail);

    /// <summary>
    /// Creates an error result with an explicit status code, title, and optional detail (Problem Details).
    /// </summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="title">The short, human-readable problem title.</param>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static SliceResult Problem(int status, string title, string? detail = null) =>
        new(status, isSuccess: false, SliceResultKind.StatusOnly, location: null, contentType: null, body: null, problemTitle: title, detail);
}
