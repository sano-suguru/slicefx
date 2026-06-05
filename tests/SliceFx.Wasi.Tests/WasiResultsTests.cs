using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SliceFx.Wasi.Tests;

public sealed class WasiResultsTests
{
    [Fact]
    public void Ok_returns_empty_body()
    {
        var response = WasiResults.Ok();

        Assert.Equal(200, response.Status);
        Assert.False(response.Headers.ContainsKey("Content-Type"));
        Assert.Empty(response.Body);
    }

    [Fact]
    public void Ok_with_json_type_info_serializes_null_as_json_null()
    {
        var response = WasiResults.Ok(null!, SliceResultTestJsonContext.Default.String);

        Assert.Equal(200, response.Status);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Equal("null", BodyText(response));
    }

    [Fact]
    public void Created_returns_location_with_empty_body()
    {
        var response = WasiResults.Created("/users/1");

        Assert.Equal(201, response.Status);
        Assert.Equal("/users/1", response.Headers["Location"]);
        Assert.False(response.Headers.ContainsKey("Content-Type"));
        Assert.Empty(response.Body);
    }

    [Fact]
    public void Created_with_json_type_info_serializes_null_as_json_null()
    {
        var response = WasiResults.Created("/users/1", null!, SliceResultTestJsonContext.Default.String);

        Assert.Equal(201, response.Status);
        Assert.Equal("/users/1", response.Headers["Location"]);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Equal("null", BodyText(response));
    }

    [Fact]
    public void Json_with_json_type_info_serializes_null_as_json_null()
    {
        var response = WasiResults.Json(202, null!, SliceResultTestJsonContext.Default.String);

        Assert.Equal(202, response.Status);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Equal("null", BodyText(response));
    }

    [Fact]
    public void Problem_returns_camel_case_problem_details()
    {
        var response = WasiResults.Problem(404, "Not Found", "Missing");

        Assert.Equal(404, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("Not Found", document.RootElement.GetProperty("title").GetString());
        Assert.Equal(404, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Missing", document.RootElement.GetProperty("detail").GetString());
        Assert.False(document.RootElement.TryGetProperty("Title", out _));
    }

    [Fact]
    public void ValidationProblem_returns_camel_case_problem_details_without_changing_error_keys()
    {
        var response = WasiResults.ValidationProblem(
            new Dictionary<string, string[]> { ["Name"] = ["Name is required."] });

        Assert.Equal(400, response.Status);
        Assert.Equal("application/problem+json", response.Headers["Content-Type"]);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("One or more validation errors occurred.", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("Name is required.", document.RootElement.GetProperty("errors").GetProperty("Name")[0].GetString());
        Assert.False(document.RootElement.GetProperty("errors").TryGetProperty("name", out _));
    }

    private static string BodyText(WasiResponse response)
        => Encoding.UTF8.GetString(response.Body);
}

[JsonSerializable(typeof(string))]
internal sealed partial class SliceResultTestJsonContext : JsonSerializerContext;
