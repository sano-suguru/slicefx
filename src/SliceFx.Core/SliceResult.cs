// CA1000: Static factory methods on a generic type are intentional for the fluent API pattern
// SliceResult<T>.Ok(value), SliceResult<T>.NotFound(), etc.
#pragma warning disable CA1000

namespace SliceFx;

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
/// <para>
/// <strong>Naming caution:</strong> <c>SliceFx.SliceResult&lt;T&gt;</c> (this type, arity 1) and
/// <c>SliceFx.Wasi.SliceResult</c> (a static factory class, arity 0) coexist by arity distinction
/// and can appear in the same file without ambiguity. However, the non-generic
/// <see cref="SliceResult"/> struct (arity 0, in this namespace) conflicts with
/// <c>SliceFx.Wasi.SliceResult</c> when both namespaces are imported. See <see cref="SliceResult"/>
/// for details.
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
/// <para>
/// <strong>Naming caution (CS0104):</strong> <c>SliceFx.SliceResult</c> (this struct, arity 0)
/// has the same simple name and arity as <c>SliceFx.Wasi.SliceResult</c> (a static factory class).
/// Having both <c>using SliceFx;</c> and <c>using SliceFx.Wasi;</c> in the same file and then
/// writing bare <c>SliceResult</c> causes a CS0104 ambiguity error. To avoid the conflict:
/// </para>
/// <list type="bullet">
///   <item>In files that use the non-generic <see cref="SliceResult"/> struct, remove
///   <c>using SliceFx.Wasi;</c>.</item>
///   <item>If both types are genuinely needed, qualify one:
///   <c>global::SliceFx.Wasi.SliceResult.Problem(...)</c>.</item>
/// </list>
/// <para>
/// The generic <c>SliceResult&lt;T&gt;</c> (arity 1) does NOT conflict with
/// <c>SliceFx.Wasi.SliceResult</c> (arity 0) — C# resolves by arity.
/// </para>
/// </remarks>
public readonly struct SliceResult
{
    // Single private constructor: all fields are always set here.
    private SliceResult(
        int status,
        bool isSuccess,
        string? location,
        string? problemTitle,
        string? problemDetail)
    {
        Status = status;
        IsSuccess = isSuccess;
        Location = location;
        ProblemTitle = problemTitle;
        ProblemDetail = problemDetail;
    }

    /// <summary>The HTTP status code for this result.</summary>
    public int Status { get; }

    /// <summary><c>true</c> when the operation succeeded; <c>false</c> for error results.</summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// The <c>Location</c> header value for 201 Created results; <c>null</c> otherwise.
    /// When non-null, the WASI translation always emits status 201.
    /// </summary>
    public string? Location { get; }

    /// <summary>The problem title for error results; <c>null</c> for success results.</summary>
    public string? ProblemTitle { get; }

    /// <summary>Optional problem detail for error results; <c>null</c> when not provided.</summary>
    public string? ProblemDetail { get; }

    /// <summary>Creates a 200 OK result (no body).</summary>
    public static SliceResult Ok() =>
        new(200, isSuccess: true, location: null, problemTitle: null, problemDetail: null);

    /// <summary>Creates a 204 No Content result.</summary>
    public static SliceResult NoContent() =>
        new(204, isSuccess: true, location: null, problemTitle: null, problemDetail: null);

    /// <summary>
    /// Creates a 201 Created result with a <c>Location</c> header (no body).
    /// </summary>
    /// <param name="location">The resource location for the <c>Location</c> header.</param>
    /// <remarks>A non-null <see cref="Location"/> always implies status 201 in the WASI translation.</remarks>
    public static SliceResult Created(string location) =>
        new(201, isSuccess: true, location, problemTitle: null, problemDetail: null);

    /// <summary>Creates a 404 Not Found error result (Problem Details, <c>application/problem+json</c>).</summary>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static SliceResult NotFound(string? detail = null) =>
        new(404, isSuccess: false, location: null, problemTitle: "Not Found", detail);

    /// <summary>Creates a 401 Unauthorized error result (Problem Details, <c>application/problem+json</c>).</summary>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static SliceResult Unauthorized(string? detail = null) =>
        new(401, isSuccess: false, location: null, problemTitle: "Unauthorized", detail);

    /// <summary>Creates a 400 Bad Request error result (Problem Details, <c>application/problem+json</c>).</summary>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static SliceResult BadRequest(string? detail = null) =>
        new(400, isSuccess: false, location: null, problemTitle: "Bad Request", detail);

    /// <summary>
    /// Creates an error result with an explicit status code, title, and optional detail (Problem Details).
    /// </summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="title">The short, human-readable problem title.</param>
    /// <param name="detail">Optional detail about the specific problem occurrence.</param>
    public static SliceResult Problem(int status, string title, string? detail = null) =>
        new(status, isSuccess: false, location: null, problemTitle: title, detail);
}
