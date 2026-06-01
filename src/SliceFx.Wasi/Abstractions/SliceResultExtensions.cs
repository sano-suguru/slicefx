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
    /// Translates a non-generic (status-only) <see cref="SliceResult"/> to a
    /// <see cref="WasiResponse"/>. No <c>JsonTypeInfo</c> is needed because success responses
    /// carry no body.
    /// </summary>
    /// <param name="result">The result to translate.</param>
    /// <returns>A <see cref="WasiResponse"/> that represents the result.</returns>
    public static WasiResponse ToWasiResponse(this SliceResult result)
    {
        if (!result.IsSuccess)
        {
            return WasiResults.Problem(result.Status, result.ProblemTitle!, result.ProblemDetail);
        }

        if (result.Location is not null)
        {
            // 201 Created, no body
            return WasiResults.Created(result.Location);
        }

        // 200 OK or 204 No Content — both are body-less in the non-generic case
        return new WasiResponse(result.Status, s_emptyHeaders, s_emptyBody);
    }

    private static readonly IReadOnlyDictionary<string, string> s_emptyHeaders =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly byte[] s_emptyBody = [];
}
