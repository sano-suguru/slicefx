using System.Text.Json;

namespace SliceFx.Lambda.FunctionPerFeature.Tests;

public sealed class LambdaResponseFactoryTests
{
    [Fact]
    public void Ok_returns_json_response()
    {
        var response = LambdaResponseFactory.Ok(new Person("Alice", 30), LambdaTestJsonContext.Default.Person);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Equal(/*lang=json,strict*/ """{"name":"Alice","age":30}""", response.Body);
    }

    [Fact]
    public void Json_serializes_null_as_json_null()
    {
        var response = LambdaResponseFactory.Json(200, null!, LambdaTestJsonContext.Default.String);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Equal("null", response.Body);
    }

    [Fact]
    public void NoContent_returns_empty_204_response()
    {
        var response = LambdaResponseFactory.NoContent();

        Assert.Equal(204, response.StatusCode);
        Assert.Null(response.Headers);
        Assert.Null(response.Body);
    }

    [Fact]
    public void Problem_returns_problem_details_response()
    {
        var response = LambdaResponseFactory.Problem(404, "Not Found", "Missing");

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("Not Found", document.RootElement.GetProperty("title").GetString());
        Assert.Equal(404, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Missing", document.RootElement.GetProperty("detail").GetString());
        Assert.False(document.RootElement.TryGetProperty("Title", out _));
    }

    [Fact]
    public void ValidationProblem_returns_validation_problem_response_without_changing_error_keys()
    {
        var response = LambdaResponseFactory.ValidationProblem(
            new Dictionary<string, string[]> { ["Name"] = ["Name is required."] });

        Assert.Equal(400, response.StatusCode);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("One or more validation errors occurred.", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("Name is required.", document.RootElement.GetProperty("errors").GetProperty("Name")[0].GetString());
        Assert.False(document.RootElement.GetProperty("errors").TryGetProperty("name", out _));
    }
}
