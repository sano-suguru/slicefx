using System.Text;

namespace SliceFx.Core.Tests;

/// <summary>
/// Tests for non-generic <see cref="SliceResult"/> factories — verifies <c>Kind</c>,
/// <c>Status</c>, <c>ContentType</c>, <c>Body</c>, and <c>Location</c> for each factory.
/// </summary>
public sealed class SliceResultTests
{
    // ── Existing status-only factories ──────────────────────────────────────────

    [Fact]
    public void Ok_has_StatusOnly_kind_and_200()
    {
        var r = SliceResult.Ok();
        Assert.Equal(SliceResultKind.StatusOnly, r.Kind);
        Assert.Equal(200, r.Status);
        Assert.True(r.IsSuccess);
        Assert.Null(r.ContentType);
        Assert.Null(r.Body);
    }

    [Fact]
    public void NoContent_has_StatusOnly_kind_and_204()
    {
        var r = SliceResult.NoContent();
        Assert.Equal(SliceResultKind.StatusOnly, r.Kind);
        Assert.Equal(204, r.Status);
        Assert.True(r.IsSuccess);
    }

    [Fact]
    public void Created_has_StatusOnly_kind_and_201_with_location()
    {
        var r = SliceResult.Created("/items/1");
        Assert.Equal(SliceResultKind.StatusOnly, r.Kind);
        Assert.Equal(201, r.Status);
        Assert.True(r.IsSuccess);
        Assert.Equal("/items/1", r.Location);
        Assert.Null(r.Body);
    }

    [Fact]
    public void NotFound_is_error_with_404()
    {
        var r = SliceResult.NotFound("item missing");
        Assert.Equal(SliceResultKind.StatusOnly, r.Kind);
        Assert.Equal(404, r.Status);
        Assert.False(r.IsSuccess);
        Assert.Equal("Not Found", r.ProblemTitle);
        Assert.Equal("item missing", r.ProblemDetail);
    }

    [Fact]
    public void Unauthorized_is_error_with_401()
    {
        var r = SliceResult.Unauthorized();
        Assert.False(r.IsSuccess);
        Assert.Equal(401, r.Status);
    }

    [Fact]
    public void BadRequest_is_error_with_400()
    {
        var r = SliceResult.BadRequest("bad input");
        Assert.False(r.IsSuccess);
        Assert.Equal(400, r.Status);
        Assert.Equal("bad input", r.ProblemDetail);
    }

    [Fact]
    public void Problem_is_error_with_custom_status()
    {
        var r = SliceResult.Problem(503, "Service Unavailable", "overloaded");
        Assert.False(r.IsSuccess);
        Assert.Equal(503, r.Status);
        Assert.Equal("Service Unavailable", r.ProblemTitle);
        Assert.Equal("overloaded", r.ProblemDetail);
    }

    [Fact]
    public void ServiceUnavailable_is_error_with_503()
    {
        var r = SliceResult.ServiceUnavailable("db is down");
        Assert.Equal(SliceResultKind.StatusOnly, r.Kind);
        Assert.False(r.IsSuccess);
        Assert.Equal(503, r.Status);
        Assert.Equal("Service Unavailable", r.ProblemTitle);
        Assert.Equal("db is down", r.ProblemDetail);
    }

    [Fact]
    public void Conflict_is_error_with_409()
    {
        var r = SliceResult.Conflict("code already exists");
        Assert.Equal(SliceResultKind.StatusOnly, r.Kind);
        Assert.False(r.IsSuccess);
        Assert.Equal(409, r.Status);
        Assert.Equal("Conflict", r.ProblemTitle);
        Assert.Equal("code already exists", r.ProblemDetail);
    }

    [Fact]
    public void Forbidden_is_error_with_403()
    {
        var r = SliceResult.Forbidden("not your resource");
        Assert.Equal(SliceResultKind.StatusOnly, r.Kind);
        Assert.False(r.IsSuccess);
        Assert.Equal(403, r.Status);
        Assert.Equal("Forbidden", r.ProblemTitle);
        Assert.Equal("not your resource", r.ProblemDetail);
    }

    [Fact]
    public void UnprocessableEntity_is_error_with_422()
    {
        var r = SliceResult.UnprocessableEntity("target URL is a private IP");
        Assert.Equal(SliceResultKind.StatusOnly, r.Kind);
        Assert.False(r.IsSuccess);
        Assert.Equal(422, r.Status);
        Assert.Equal("Unprocessable Entity", r.ProblemTitle);
        Assert.Equal("target URL is a private IP", r.ProblemDetail);
    }

    [Fact]
    public void ServiceUnavailable_default_detail_is_null()
    {
        var r = SliceResult.ServiceUnavailable();
        Assert.Null(r.ProblemDetail);
    }

    // ── Redirect ────────────────────────────────────────────────────────────────

    [Fact]
    public void Redirect_defaults_to_302_and_Redirect_kind()
    {
        var r = SliceResult.Redirect("/new-path");
        Assert.Equal(SliceResultKind.Redirect, r.Kind);
        Assert.Equal(302, r.Status);
        Assert.True(r.IsSuccess);
        Assert.Equal("/new-path", r.Location);
        Assert.Null(r.Body);
        Assert.Null(r.ContentType);
    }

    [Fact]
    public void Redirect_permanent_sets_301()
    {
        var r = SliceResult.Redirect("/new-path", permanent: true);
        Assert.Equal(SliceResultKind.Redirect, r.Kind);
        Assert.Equal(301, r.Status);
        Assert.Equal("/new-path", r.Location);
    }

    // ── Content / Html / Text ───────────────────────────────────────────────────

    [Fact]
    public void Html_sets_RawBody_kind_and_html_content_type()
    {
        var r = SliceResult.Html("<h1>Hello</h1>");
        Assert.Equal(SliceResultKind.RawBody, r.Kind);
        Assert.Equal(200, r.Status);
        Assert.True(r.IsSuccess);
        Assert.Equal("text/html; charset=utf-8", r.ContentType);
        Assert.Equal("<h1>Hello</h1>", Encoding.UTF8.GetString(r.Body!));
    }

    [Fact]
    public void Text_sets_RawBody_kind_and_plain_content_type()
    {
        var r = SliceResult.Text("hello world");
        Assert.Equal(SliceResultKind.RawBody, r.Kind);
        Assert.Equal(200, r.Status);
        Assert.Equal("text/plain; charset=utf-8", r.ContentType);
        Assert.Equal("hello world", Encoding.UTF8.GetString(r.Body!));
    }

    [Fact]
    public void Content_custom_content_type_and_default_200()
    {
        var r = SliceResult.Content("<root/>", "application/xml");
        Assert.Equal(SliceResultKind.RawBody, r.Kind);
        Assert.Equal(200, r.Status);
        Assert.Equal("application/xml", r.ContentType);
        Assert.Equal("<root/>", Encoding.UTF8.GetString(r.Body!));
    }

    [Fact]
    public void Content_accepts_custom_status()
    {
        var r = SliceResult.Content("partial body", "text/plain", status: 206);
        Assert.Equal(206, r.Status);
        Assert.Equal(SliceResultKind.RawBody, r.Kind);
    }

    // ── Bytes ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Bytes_sets_RawBody_kind_and_preserves_array()
    {
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
        var r = SliceResult.Bytes(data, "image/png");
        Assert.Equal(SliceResultKind.RawBody, r.Kind);
        Assert.Equal(200, r.Status);
        Assert.True(r.IsSuccess);
        Assert.Equal("image/png", r.ContentType);
        Assert.Equal(data, r.Body);
    }

    [Fact]
    public void Bytes_accepts_custom_status()
    {
        var r = SliceResult.Bytes([0x01, 0x02], "application/octet-stream", status: 202);
        Assert.Equal(202, r.Status);
    }
}
