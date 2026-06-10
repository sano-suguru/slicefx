using System.Text.Json.Serialization.Metadata;
using Amazon.Lambda.APIGatewayEvents;

namespace SliceFx.Lambda.FunctionPerFeature;

/// <summary>
/// Extension methods that translate <see cref="SliceResult"/> and <see cref="SliceResult{T}"/>
/// values to <see cref="APIGatewayHttpApiV2ProxyResponse"/> instances for the Lambda
/// function-per-feature path.
/// </summary>
/// <remarks>
/// These methods are used by the source-generated Lambda function-per-feature dispatch code.
/// All Lambda responses use <c>IsBase64Encoded = true</c> for raw-body results to handle
/// both text and binary content uniformly.
/// </remarks>
public static class SliceResultExtensions
{
    /// <summary>
    /// Translates a non-generic <see cref="SliceResult"/> to an
    /// <see cref="APIGatewayHttpApiV2ProxyResponse"/>.
    /// </summary>
    /// <param name="result">The result to translate.</param>
    public static APIGatewayHttpApiV2ProxyResponse ToLambdaResponse(this SliceResult result)
    {
        if (!result.IsSuccess)
        {
            return LambdaResponseFactory.Problem(result.Status, result.ProblemTitle!, result.ProblemDetail);
        }

        switch (result.Kind)
        {
            case SliceResultKind.Redirect:
                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = result.Status,
                    Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Location"] = result.Location! },
                };

            case SliceResultKind.RawBody:
                // Always base64-encode so binary and text body are handled uniformly.
                return new APIGatewayHttpApiV2ProxyResponse
                {
                    StatusCode = result.Status,
                    Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = result.ContentType! },
                    Body = Convert.ToBase64String(result.Body!),
                    IsBase64Encoded = true,
                };

            case SliceResultKind.StatusOnly:
            default:
                if (result.Location is not null)
                {
                    // 201 Created, no body
                    return new APIGatewayHttpApiV2ProxyResponse
                    {
                        StatusCode = 201,
                        Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Location"] = result.Location },
                    };
                }

                return new APIGatewayHttpApiV2ProxyResponse { StatusCode = result.Status };
        }
    }

    /// <summary>
    /// Translates a typed <see cref="SliceResult{T}"/> to an
    /// <see cref="APIGatewayHttpApiV2ProxyResponse"/>.
    /// </summary>
    /// <typeparam name="T">The type of the success body.</typeparam>
    /// <param name="result">The result to translate.</param>
    /// <param name="jsonTypeInfo">
    /// The source-generated JSON metadata used to serialize the success body.
    /// Provided automatically by the source-generated dispatch code via <c>__JsonTypeInfo&lt;T&gt;()</c>.
    /// </param>
    public static APIGatewayHttpApiV2ProxyResponse ToLambdaResponse<T>(
        this SliceResult<T> result,
        JsonTypeInfo<T> jsonTypeInfo)
    {
        if (!result.IsSuccess)
        {
            return LambdaResponseFactory.Problem(result.Status, result.ProblemTitle!, result.ProblemDetail);
        }

        if (!result.HasBody)
        {
            return new APIGatewayHttpApiV2ProxyResponse { StatusCode = result.Status };
        }

        if (result.Location is not null)
        {
            return LambdaResponseFactory.Created(result.Location, result.Value!, jsonTypeInfo);
        }

        return LambdaResponseFactory.Json(result.Status, result.Value!, jsonTypeInfo);
    }
}
