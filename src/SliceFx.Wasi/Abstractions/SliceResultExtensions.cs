using System.Text.Json.Serialization.Metadata;

namespace SliceFx.Wasi;

/// <summary>
/// Extension methods that translate <see cref="SliceResult{T}"/> and
/// <see cref="SliceResult"/> values to <see cref="WasiResponse"/> instances.
/// </summary>
/// <remarks>
/// These methods are used by the source-generated WASI dispatch table.
/// They delegate to <see cref="WasiResults"/> static factory methods for serialization,
/// keeping all JSON logic inside <c>SliceFx.Wasi</c>.
/// </remarks>
public static class SliceResultExtensions
{
    /// <summary>
    /// Translates a typed <see cref="SliceResult{T}"/> to a <see cref="WasiResponse"/>.
    /// </summary>
    /// <typeparam name="T">The type of the success body.</typeparam>
    /// <param name="result">The result to translate.</param>
    /// <param name="jsonTypeInfo">
    /// The source-generated JSON metadata used to serialize the success body.
    /// Provided automatically by the source-generated dispatch code via <c>__JsonTypeInfo&lt;T&gt;()</c>.
    /// </param>
    /// <returns>A <see cref="WasiResponse"/> that represents the result.</returns>
    public static WasiResponse ToWasiResponse<T>(this SliceResult<T> result, JsonTypeInfo<T> jsonTypeInfo)
    {
        if (!result.IsSuccess)
        {
            return WasiResults.Problem(result.Status, result.ProblemTitle!, result.ProblemDetail);
        }

        if (!result.HasBody)
        {
            // 204 No Content (or other no-body success, e.g. triggered from NoContent())
            return WasiResults.NoContent();
        }

        if (result.Location is not null)
        {
            // 201 Created with body — Location is always status 201 (SliceResult.Created hardcodes 201)
            return WasiResults.Created(result.Location, result.Value!, jsonTypeInfo);
        }

        // 200 OK or other status with JSON body
        return WasiResults.Json(result.Status, result.Value!, jsonTypeInfo);
    }

    /// <summary>
    /// Translates a non-generic <see cref="SliceResult"/> to a <see cref="WasiResponse"/>.
    /// No <c>JsonTypeInfo</c> is needed because the non-generic result carries no JSON body.
    /// </summary>
    /// <param name="result">The result to translate.</param>
    /// <returns>A <see cref="WasiResponse"/> that represents the result.</returns>
    public static WasiResponse ToWasiResponse(this SliceResult result)
    {
        if (!result.IsSuccess)
        {
            return WasiResults.Problem(result.Status, result.ProblemTitle!, result.ProblemDetail);
        }

        switch (result.Kind)
        {
            case SliceResultKind.Redirect:
                // 301 or 302 — Location header set, empty body.
                return new WasiResponse(
                    result.Status,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["Location"] = result.Location! },
                    s_emptyBody);

            case SliceResultKind.RawBody:
                // Html / Text / Content / Bytes — pre-encoded body with explicit Content-Type.
                return WasiResults.Bytes(result.Status, result.ContentType!, result.Body!);

            case SliceResultKind.StatusOnly:
            default:
                // StatusOnly: 201 Created (Location), or status-only 200/204.
                if (result.Location is not null)
                {
                    return WasiResults.Created(result.Location);
                }

                return new WasiResponse(result.Status, s_emptyHeaders, s_emptyBody);
        }
    }

    private static readonly IReadOnlyDictionary<string, string> s_emptyHeaders =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly byte[] s_emptyBody = [];
}
