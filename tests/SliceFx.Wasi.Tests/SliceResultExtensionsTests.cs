using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SliceFx.Wasi.Tests;

/// <summary>
/// Tests for <see cref="SliceResultExtensions"/> — translation of host-neutral
/// <see cref="SliceResult{T}"/> / <see cref="SliceResult"/> to <see cref="WasiResponse"/>.
/// </summary>
public sealed class SliceResultExtensionsTests
{
    // ── Generic SliceResult<T>.ToWasiResponse(JsonTypeInfo<T>) ──────────────────

    [Fact]
    public void Generic_Ok_produces_200_json_body()
    {
        var result = SliceResult<ExtTestDto>.Ok(new ExtTestDto("hello"));

        var response = result.ToWasiResponse(ExtTestJsonContext.Default.ExtTestDto);

        Assert.Equal(200, response.Status);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Equal(/*lang=json,strict*/ "{\"value\":\"hello\"}", BodyText(response));
    }

    [Fact]
    public void Generic_Created_produces_201_with_location_and_body()
    {
        var result = SliceResult<ExtTestDto>.Created(new ExtTestDto("new"), "/items/42");

        var response = result.ToWasiResponse(ExtTestJsonContext.Default.ExtTestDto);

        Assert.Equal(201, response.Status);
        Assert.Equal("/items/42", response.Headers["Location"]);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Equal(/*lang=json,strict*/ "{\"value\":\"new\"}", BodyText(response));
    }

    [Fact]
    public void Generic_NoContent_produces_204_empty_body()
    {
        var result = SliceResult<ExtTestDto>.NoContent();

        var response = result.ToWasiResponse(ExtTestJsonContext.Default.ExtTestDto);

        Assert.Equal(204, response.Status);
        Assert.Empty(response.Body);
    }

    [Fact]
    public void Generic_NotFound_produces_404_problem_json()
    {
        var result = SliceResult<ExtTestDto>.NotFound("Item 42 not found.");

        var response = result.ToWasiResponse(ExtTestJsonContext.Default.ExtTestDto);

        Assert.Equal(404, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Not Found", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(404, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Item 42 not found.", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void Generic_Unauthorized_produces_401_problem_json()
    {
        var result = SliceResult<ExtTestDto>.Unauthorized();

        var response = result.ToWasiResponse(ExtTestJsonContext.Default.ExtTestDto);

        Assert.Equal(401, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Unauthorized", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(401, doc.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public void Generic_BadRequest_produces_400_problem_json()
    {
        var result = SliceResult<ExtTestDto>.BadRequest("Invalid payload.");

        var response = result.ToWasiResponse(ExtTestJsonContext.Default.ExtTestDto);

        Assert.Equal(400, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Bad Request", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("Invalid payload.", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void Generic_Problem_produces_custom_status_problem_json()
    {
        var result = SliceResult<ExtTestDto>.Problem(503, "Service Unavailable", "Try again later.");

        var response = result.ToWasiResponse(ExtTestJsonContext.Default.ExtTestDto);

        Assert.Equal(503, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Service Unavailable", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(503, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Try again later.", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void Generic_ServiceUnavailable_produces_503_problem_json_no_body()
    {
        // Verifies that the new named factory delegates to Problem and the error path writes
        // Problem Details (not the typed body) via ToWasiResponse.
        var result = SliceResult<ExtTestDto>.ServiceUnavailable("db is down");

        var response = result.ToWasiResponse(ExtTestJsonContext.Default.ExtTestDto);

        Assert.Equal(503, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Service Unavailable", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(503, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("db is down", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void Generic_Conflict_produces_409_problem_json()
    {
        var result = SliceResult<ExtTestDto>.Conflict("code already taken");

        var response = result.ToWasiResponse(ExtTestJsonContext.Default.ExtTestDto);

        Assert.Equal(409, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Conflict", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void Generic_Forbidden_produces_403_problem_json()
    {
        var result = SliceResult<ExtTestDto>.Forbidden("not your resource");

        var response = result.ToWasiResponse(ExtTestJsonContext.Default.ExtTestDto);

        Assert.Equal(403, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Forbidden", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void Generic_UnprocessableEntity_produces_422_problem_json()
    {
        var result = SliceResult<ExtTestDto>.UnprocessableEntity("private IP not allowed");

        var response = result.ToWasiResponse(ExtTestJsonContext.Default.ExtTestDto);

        Assert.Equal(422, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Unprocessable Entity", doc.RootElement.GetProperty("title").GetString());
    }

    // ── Non-generic SliceResult.ToWasiResponse() ─────────────────────────────

    [Fact]
    public void NonGeneric_NoContent_produces_204_empty_body()
    {
        var result = SliceResult.NoContent();

        var response = result.ToWasiResponse();

        Assert.Equal(204, response.Status);
        Assert.Empty(response.Body);
    }

    [Fact]
    public void NonGeneric_Ok_produces_200_empty_body()
    {
        var result = SliceResult.Ok();

        var response = result.ToWasiResponse();

        Assert.Equal(200, response.Status);
        Assert.Empty(response.Body);
    }

    [Fact]
    public void NonGeneric_Created_produces_201_location_empty_body()
    {
        var result = SliceResult.Created("/items/99");

        var response = result.ToWasiResponse();

        Assert.Equal(201, response.Status);
        Assert.Equal("/items/99", response.Headers["Location"]);
        Assert.Empty(response.Body);
    }

    [Fact]
    public void NonGeneric_NotFound_produces_404_problem_json()
    {
        var result = SliceResult.NotFound("Resource missing.");

        var response = result.ToWasiResponse();

        Assert.Equal(404, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Not Found", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("Resource missing.", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void NonGeneric_Unauthorized_produces_401_problem_json()
    {
        var result = SliceResult.Unauthorized();

        var response = result.ToWasiResponse();

        Assert.Equal(401, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
    }

    [Fact]
    public void NonGeneric_Problem_produces_custom_status_problem_json()
    {
        var result = SliceResult.Problem(409, "Conflict", "Item already exists.");

        var response = result.ToWasiResponse();

        Assert.Equal(409, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Conflict", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("Item already exists.", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void NonGeneric_ServiceUnavailable_produces_503_problem_json()
    {
        var result = SliceResult.ServiceUnavailable("db unreachable");

        var response = result.ToWasiResponse();

        Assert.Equal(503, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Service Unavailable", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("db unreachable", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void NonGeneric_Conflict_produces_409_problem_json()
    {
        var result = SliceResult.Conflict("code already taken");

        var response = result.ToWasiResponse();

        Assert.Equal(409, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Conflict", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void NonGeneric_Forbidden_produces_403_problem_json()
    {
        var result = SliceResult.Forbidden("access denied");

        var response = result.ToWasiResponse();

        Assert.Equal(403, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Forbidden", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void NonGeneric_UnprocessableEntity_produces_422_problem_json()
    {
        var result = SliceResult.UnprocessableEntity("private IP not allowed");

        var response = result.ToWasiResponse();

        Assert.Equal(422, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var doc = JsonDocument.Parse(response.Body);
        Assert.Equal("Unprocessable Entity", doc.RootElement.GetProperty("title").GetString());
    }

    // ── Non-generic Redirect / Html / Text / Content / Bytes ─────────────────────

    [Fact]
    public void NonGeneric_Redirect_temporary_produces_302_location_empty_body()
    {
        var result = SliceResult.Redirect("/new-path");

        var response = result.ToWasiResponse();

        Assert.Equal(302, response.Status);
        Assert.Equal("/new-path", response.Headers["Location"]);
        Assert.Empty(response.Body);
        Assert.False(response.Headers.ContainsKey("Content-Type"));
    }

    [Fact]
    public void NonGeneric_Redirect_permanent_produces_301_location()
    {
        var result = SliceResult.Redirect("/new-path", permanent: true);

        var response = result.ToWasiResponse();

        Assert.Equal(301, response.Status);
        Assert.Equal("/new-path", response.Headers["Location"]);
    }

    [Fact]
    public void NonGeneric_Html_produces_200_html_body()
    {
        var result = SliceResult.Html("<h1>Hello</h1>");

        var response = result.ToWasiResponse();

        Assert.Equal(200, response.Status);
        Assert.Equal("text/html; charset=utf-8", response.Headers["Content-Type"]);
        Assert.Equal("<h1>Hello</h1>", BodyText(response));
    }

    [Fact]
    public void NonGeneric_Text_produces_200_plaintext_body()
    {
        var result = SliceResult.Text("hello world");

        var response = result.ToWasiResponse();

        Assert.Equal(200, response.Status);
        Assert.Equal("text/plain; charset=utf-8", response.Headers["Content-Type"]);
        Assert.Equal("hello world", BodyText(response));
    }

    [Fact]
    public void NonGeneric_Content_custom_type_produces_correct_body()
    {
        var result = SliceResult.Content("<root/>", "application/xml");

        var response = result.ToWasiResponse();

        Assert.Equal(200, response.Status);
        Assert.Equal("application/xml", response.Headers["Content-Type"]);
        Assert.Equal("<root/>", BodyText(response));
    }

    [Fact]
    public void NonGeneric_Bytes_produces_raw_bytes_with_correct_content_type()
    {
        byte[] data = [0x89, 0x50, 0x4E, 0x47]; // PNG magic bytes
        var result = SliceResult.Bytes(data, "image/png");

        var response = result.ToWasiResponse();

        Assert.Equal(200, response.Status);
        Assert.Equal("image/png", response.Headers["Content-Type"]);
        Assert.Equal(data, response.Body);
    }

    private static string BodyText(WasiResponse response)
        => Encoding.UTF8.GetString(response.Body);
}

internal sealed record ExtTestDto(string Value);

[JsonSerializable(typeof(ExtTestDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ExtTestJsonContext : JsonSerializerContext;
