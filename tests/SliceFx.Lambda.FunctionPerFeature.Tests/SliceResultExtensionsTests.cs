using System.Text;
using System.Text.Json;

namespace SliceFx.Lambda.FunctionPerFeature.Tests;

/// <summary>
/// Tests for <see cref="SliceResultExtensions"/> — translation of host-neutral
/// <see cref="SliceResult"/> / <see cref="SliceResult{T}"/> to
/// <see cref="Amazon.Lambda.APIGatewayEvents.APIGatewayHttpApiV2ProxyResponse"/>.
/// </summary>
public sealed class SliceResultExtensionsTests
{
    // ── Non-generic SliceResult.ToLambdaResponse() ───────────────────────────────

    [Fact]
    public void NonGeneric_Redirect_temporary_produces_302_with_location()
    {
        var result = SliceResult.Redirect("/new-path");

        var response = result.ToLambdaResponse();

        Assert.Equal(302, response.StatusCode);
        Assert.Equal("/new-path", response.Headers["Location"]);
        Assert.Null(response.Body);
        Assert.False(response.Headers.ContainsKey("Content-Type"));
    }

    [Fact]
    public void NonGeneric_Redirect_permanent_produces_301()
    {
        var result = SliceResult.Redirect("/new-path", permanent: true);

        var response = result.ToLambdaResponse();

        Assert.Equal(301, response.StatusCode);
        Assert.Equal("/new-path", response.Headers["Location"]);
    }

    [Fact]
    public void NonGeneric_Html_is_base64_encoded_with_text_html_content_type()
    {
        var result = SliceResult.Html("<h1>Hello</h1>");

        var response = result.ToLambdaResponse();

        Assert.Equal(200, response.StatusCode);
        Assert.True(response.IsBase64Encoded);
        Assert.Equal("text/html; charset=utf-8", response.Headers["Content-Type"]);
        Assert.Equal("<h1>Hello</h1>", Encoding.UTF8.GetString(Convert.FromBase64String(response.Body!)));
    }

    [Fact]
    public void NonGeneric_Text_is_base64_encoded_with_text_plain_content_type()
    {
        var result = SliceResult.Text("hello world");

        var response = result.ToLambdaResponse();

        Assert.Equal(200, response.StatusCode);
        Assert.True(response.IsBase64Encoded);
        Assert.Equal("text/plain; charset=utf-8", response.Headers["Content-Type"]);
        Assert.Equal("hello world", Encoding.UTF8.GetString(Convert.FromBase64String(response.Body!)));
    }

    [Fact]
    public void NonGeneric_Bytes_is_base64_encoded_with_binary_content_type()
    {
        byte[] data = [0x89, 0x50, 0x4E, 0x47]; // PNG magic bytes
        var result = SliceResult.Bytes(data, "image/png");

        var response = result.ToLambdaResponse();

        Assert.Equal(200, response.StatusCode);
        Assert.True(response.IsBase64Encoded);
        Assert.Equal("image/png", response.Headers["Content-Type"]);
        Assert.Equal(data, Convert.FromBase64String(response.Body!));
    }

    [Fact]
    public void NonGeneric_NoContent_produces_204_empty()
    {
        var result = SliceResult.NoContent();

        var response = result.ToLambdaResponse();

        Assert.Equal(204, response.StatusCode);
        Assert.Null(response.Headers);
        Assert.Null(response.Body);
    }

    [Fact]
    public void NonGeneric_Created_produces_201_with_location()
    {
        var result = SliceResult.Created("/items/99");

        var response = result.ToLambdaResponse();

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("/items/99", response.Headers["Location"]);
        Assert.Null(response.Body);
    }

    [Fact]
    public void NonGeneric_NotFound_produces_404_problem_json()
    {
        var result = SliceResult.NotFound("item missing");

        var response = result.ToLambdaResponse();

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body!);
        Assert.Equal("Not Found", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("item missing", doc.RootElement.GetProperty("detail").GetString());
    }

    // ── Generic SliceResult<T>.ToLambdaResponse(JsonTypeInfo<T>) ────────────────

    [Fact]
    public void Generic_Ok_produces_200_json_body()
    {
        var result = SliceResult<Person>.Ok(new Person("Alice", 30));

        var response = result.ToLambdaResponse(LambdaTestJsonContext.Default.Person);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Equal(/*lang=json,strict*/ """{"name":"Alice","age":30}""", response.Body);
    }

    [Fact]
    public void Generic_Created_produces_201_with_location_and_body()
    {
        var result = SliceResult<Person>.Created(new Person("Bob", 25), "/persons/1");

        var response = result.ToLambdaResponse(LambdaTestJsonContext.Default.Person);

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("/persons/1", response.Headers["Location"]);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Contains("\"name\":\"Bob\"", response.Body);
    }

    [Fact]
    public void Generic_NotFound_produces_404_problem_json()
    {
        var result = SliceResult<Person>.NotFound("not found");

        var response = result.ToLambdaResponse(LambdaTestJsonContext.Default.Person);

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body!);
        Assert.Equal("Not Found", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void Generic_NoContent_produces_204_empty()
    {
        var result = SliceResult<Person>.NoContent();

        var response = result.ToLambdaResponse(LambdaTestJsonContext.Default.Person);

        Assert.Equal(204, response.StatusCode);
        Assert.Null(response.Headers);
        Assert.Null(response.Body);
    }
}
